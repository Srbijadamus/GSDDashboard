using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace GSDDashboard.API.Modules.WicSchedule;

public record WicDayDto(string Date, string DayOfWeek, string? SupportLocation, string? WicOpeningHours, string? WorkingShift, bool IsOnSite, bool IsOffDay, string? Task, bool IsGSDDay);
public record WicAgentScheduleDto(string EmployeeId, string FullName, string? TeamLeadName, List<string> AssignedLocations, List<WicDayDto> Days);
public record WicLocationDayDto(string Date, string DayOfWeek, bool IsOpen, string? OpenTime, string? CloseTime, string? OpenTime2, string? CloseTime2, string? RawSchedule, int AgentCount, List<string> AgentNames);
public record WicLocationScheduleDto(string LocationCode, string DisplayName, string? City, int TotalAssignedAgents, List<WicLocationDayDto> Days);
public record WicDayHoursDto(int DayOfWeek, string DayName, bool IsClosed, string? OpenTime, string? CloseTime, string? OpenTime2, string? CloseTime2, string? RawSchedule, int? MinRequired = null);
public record MinRequiredDto(int? Value);
public record WicOpeningHoursDto(string LocationCode, string DisplayName, string? City, int AssignedAgentCount, List<WicDayHoursDto> WeeklyHours);

public record UpdateScheduleDayDto(
    int DayOfWeek, bool IsClosed,
    string? OpenTime = null, string? CloseTime = null,
    string? OpenTime2 = null, string? CloseTime2 = null);

public record UpdateScheduleRequestDto(
    string EffectiveFrom,
    string? ChangeNote,
    bool CloseEntireCentre,
    List<UpdateScheduleDayDto> Days);

public record ScheduleConsequenceDto(
    string EmployeeId, string? FullName,
    string Date, string Weekday,
    string? SupportLocation, string? WorkingShift, string Issue);

public record UpdateScheduleResponseDto(
    bool Saved, string EffectiveFrom, int RowsInserted,
    List<ScheduleConsequenceDto> Consequences);

public record CleanupClosedDaysRequestDto(string EffectiveFrom);

public record CleanupAgentDayDto(string EmployeeId, string? FullName, string Date, string Weekday);

public record CleanupClosedDaysResponseDto(
    bool Success, string LocationCode, string Cutoff,
    int WicShiftEntriesRemoved, int ShiftEntriesRemoved,
    List<CleanupAgentDayDto> AgentsLeftWithNothing,
    List<CleanupAgentDayDto> SplitDutyKept);

public record ClosedDayDutyDto(
    string LocationCode, string DisplayName,
    string EmployeeId, string? FullName,
    string Date, string Weekday, string? WorkingShift,
    bool HasShiftEntryWicDuty, string Source);

public class WicScheduleService
{
    private readonly GSDContext _db;
    private readonly ILogger<WicScheduleService> _log;
    public WicScheduleService(GSDContext db, ILogger<WicScheduleService> log) { _db = db; _log = log; }

    private static string DayName(int d) => d switch {
        1 => "Mon", 2 => "Tue", 3 => "Wed", 4 => "Thu",
        5 => "Fri", 6 => "Sat", 7 => "Sun", _ => "?"
    };

    private static string GetDow(DateOnly d) => d.DayOfWeek switch {
        DayOfWeek.Monday => "Mon", DayOfWeek.Tuesday => "Tue",
        DayOfWeek.Wednesday => "Wed", DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri", DayOfWeek.Saturday => "Sat",
        DayOfWeek.Sunday => "Sun", _ => "?"
    };

    public async Task<List<WicAgentScheduleDto>> GetAgentScheduleAsync(string from, string to, string? employeeId)
    {
        if (!DateOnly.TryParse(from, out var fromDate)) fromDate = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(to,   out var toDate))   toDate   = fromDate.AddDays(13);

        var wicQuery = _db.WicShiftEntries.Where(w => w.ShiftDate >= fromDate && w.ShiftDate <= toDate);
        if (!string.IsNullOrEmpty(employeeId))
            wicQuery = wicQuery.Where(w => w.EmployeeId == employeeId);
        var wicEntries = await wicQuery.ToListAsync();

        var wicEmployeeIds = wicEntries.Select(w => w.EmployeeId).Distinct().ToList();
        if (!string.IsNullOrEmpty(employeeId)) wicEmployeeIds = new List<string> { employeeId };

        var employees = await _db.Employees
            .Where(e => wicEmployeeIds.Contains(e.EmployeeId) && e.IsActive)
            .ToListAsync();

        var assignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();

        var result = new List<WicAgentScheduleDto>();
        foreach (var emp in employees.OrderBy(e => e.TeamLeadName).ThenBy(e => e.FullName))
        {
            var empAssignments = assignments.Where(a => a.EmployeeName == emp.FullName).Select(a => a.LocationCode).ToList();
            var empEntries = wicEntries.Where(w => w.EmployeeId == emp.EmployeeId)
                .GroupBy(w => w.ShiftDate)
                .ToDictionary(g => g.Key, g =>
                {
                    if (g.Count() > 1)
                        _log.LogWarning("WicSchedule: {Count} WicShiftEntries for {EmpId} on {Date}",
                            g.Count(), emp.EmployeeId, g.Key);
                    return ShiftDuplicateResolver.BestWicEntry(g);
                });

            var days = new List<WicDayDto>();
            for (var d = fromDate; d <= toDate; d = d.AddDays(1))
            {
                empEntries.TryGetValue(d, out var entry);
                days.Add(new WicDayDto(d.ToString("yyyy-MM-dd"), GetDow(d),
                    entry?.SupportLocation, entry?.WicOpeningHours, entry?.WorkingShift,
                    entry?.IsOnSite ?? false, entry?.IsOffDay ?? false, entry?.Task, entry?.IsGSDDay ?? false));
            }

            result.Add(new WicAgentScheduleDto(emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                emp.TeamLeadName, empAssignments, days));
        }
        return result;
    }

    public async Task<List<WicOpeningHoursDto>> GetOpeningHoursAsync(DateOnly? asOf = null)
    {
        var effectiveDate = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        var locations   = await _db.WicLocations.Where(l => l.IsActive).OrderBy(l => l.DisplayName).ToListAsync();
        var allHours    = await _db.WicOpeningHours.ToListAsync();
        var assignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();

        // Per location+day, pick the latest version whose EffectiveFrom <= today (NULL = from inception)
        var effectiveHours = allHours
            .Where(h => h.EffectiveFrom == null || h.EffectiveFrom <= effectiveDate)
            .GroupBy(h => (h.LocationCode, h.DayOfWeek))
            .Select(g => g.OrderByDescending(h => h.EffectiveFrom ?? DateOnly.MinValue).First())
            .ToList();

        return locations.Select(loc => {
            var locHours = effectiveHours
                .Where(h => h.LocationCode == loc.LocationCode)
                .OrderBy(h => h.DayOfWeek)
                .Select(h => new WicDayHoursDto(h.DayOfWeek, DayName(h.DayOfWeek), h.IsClosed,
                    h.OpenTime, h.CloseTime, h.OpenTime2, h.CloseTime2, h.RawSchedule, h.MinRequired)).ToList();
            int agentCount = assignments.Count(a =>
                a.LocationCode == loc.LocationCode ||
                a.LocationCode == loc.LocationCodeLegacy);
            return new WicOpeningHoursDto(loc.LocationCode, loc.DisplayName, loc.City, agentCount, locHours);
        }).ToList();
    }

    public async Task<string> ExportAgentsCsvAsync(string from, string to)
    {
        var data = await GetAgentScheduleAsync(from, to, null);
        if (!DateOnly.TryParse(from, out var fromDate)) fromDate = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(to,   out var toDate))   toDate   = fromDate.AddDays(13);

        var dates = new List<string>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
            dates.Add(d.ToString("dd.MM"));

        var lines = new List<string> { "Name,Team Lead,Standorte," + string.Join(",", dates) };

        foreach (var agent in data)
        {
            var cells = new List<string> {
                $"\"{agent.FullName}\"",
                $"\"{agent.TeamLeadName ?? ""}\"",
                $"\"{string.Join("|", agent.AssignedLocations)}\""
            };
            foreach (var day in agent.Days)
            {
                string cell = day.IsOffDay ? (day.WorkingShift ?? "OFF") :
                              day.IsOnSite && day.SupportLocation != null ? day.SupportLocation :
                              day.IsGSDDay ? "GSD Backlog" :
                              day.WorkingShift ?? "";
                cells.Add($"\"{cell}\"");
            }
            lines.Add(string.Join(",", cells));
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Read-only check: list every FUTURE (today or later) live WIC duty that falls on a
    /// day its location is closed, resolving the versioned WicOpeningHours per duty date
    /// (latest EffectiveFrom &lt;= duty date). Covers WicShiftEntries (IsOnSite) plus orphaned
    /// ShiftEntries WIC_DUTY rows that have no WicShiftEntries row (matched via AgentTask =
    /// location DisplayName, truncated to 20 chars as WicShiftService writes it).
    /// Never returns absences — only WIC duties are considered.
    /// </summary>
    public async Task<List<ClosedDayDutyDto>> GetClosedDayDutiesAsync(string? locationCode)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var locations = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        if (!string.IsNullOrEmpty(locationCode))
            locations = locations.Where(l => l.LocationCode == locationCode).ToList();

        var allHours = await _db.WicOpeningHours.ToListAsync();

        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.IsOnSite && w.ShiftDate >= today)
            .ToListAsync();

        var futureDutyEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= today && s.ShiftType == ShiftTypes.WicDuty)
            .ToListAsync();

        var byCode = locations.ToDictionary(l => l.LocationCode, l => l);
        var byName = locations
            .GroupBy(l => l.DisplayName)
            .ToDictionary(g => g.Key, g => g.First());

        var result  = new List<ClosedDayDutyDto>();
        var covered = new HashSet<(string Emp, DateOnly Date, string Code)>();

        foreach (var w in wicEntries)
        {
            WicLocation? loc = null;
            if (w.LocationCode != null) byCode.TryGetValue(w.LocationCode, out loc);
            if (loc == null && w.SupportLocation != null)
                byName.TryGetValue(w.SupportLocation, out loc);
            if (loc == null) continue;

            var hours = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy,
                (int)w.ShiftDate.DayOfWeek, w.ShiftDate);
            if (hours is not { IsClosed: true }) continue;

            var hasDuty = futureDutyEntries.Any(d => d.EmployeeId == w.EmployeeId && d.ShiftDate == w.ShiftDate);
            result.Add(new ClosedDayDutyDto(loc.LocationCode, loc.DisplayName, w.EmployeeId,
                null, w.ShiftDate.ToString("yyyy-MM-dd"), w.ShiftDate.DayOfWeek.ToString()[..3],
                w.WorkingShift, hasDuty, "WIC_SHIFT_ENTRY"));
            covered.Add((w.EmployeeId, w.ShiftDate, loc.LocationCode));
        }

        // Orphaned WIC_DUTY ShiftEntries (no matching WicShiftEntries row above).
        foreach (var d in futureDutyEntries)
        {
            if (string.IsNullOrEmpty(d.AgentTask)) continue;
            var loc = byName.Values.FirstOrDefault(l =>
                string.Equals(l.DisplayName, d.AgentTask, StringComparison.OrdinalIgnoreCase) ||
                (d.AgentTask.Length == 20 && l.DisplayName.StartsWith(d.AgentTask, StringComparison.OrdinalIgnoreCase)));
            if (loc == null) continue;
            if (covered.Contains((d.EmployeeId, d.ShiftDate, loc.LocationCode))) continue;

            var hours = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy,
                (int)d.ShiftDate.DayOfWeek, d.ShiftDate);
            if (hours is not { IsClosed: true }) continue;

            result.Add(new ClosedDayDutyDto(loc.LocationCode, loc.DisplayName, d.EmployeeId,
                null, d.ShiftDate.ToString("yyyy-MM-dd"), d.ShiftDate.DayOfWeek.ToString()[..3],
                d.ShiftStart != null && d.ShiftEnd != null ? $"{d.ShiftStart}-{d.ShiftEnd}" : null,
                true, "SHIFTENTRY_ONLY"));
        }

        var empIds = result.Select(r => r.EmployeeId).Distinct().ToList();
        var names = await _db.Employees.Where(e => empIds.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);

        return result
            .OrderBy(r => r.Date).ThenBy(r => r.DisplayName)
            .Select(r => r with { FullName = names.GetValueOrDefault(r.EmployeeId) })
            .ToList();
    }

    /// <summary>
    /// Confirmed cleanup after an opening-hours change: removes FUTURE duties for this
    /// location that fall on weekdays that are now closed. Rules:
    ///  - never touches the past: cutoff = max(effectiveFrom, today);
    ///  - respects EffectiveFrom: closed status is resolved per duty date from the
    ///    versioned WicOpeningHours rows (latest EffectiveFrom &lt;= duty date);
    ///  - removes both the WicShiftEntries rows AND the ShiftEntries WIC_DUTY row;
    ///  - never removes absences — only ShiftType = WIC_DUTY rows are deleted;
    ///  - split-duty guard: if the agent still has a live (IsOnSite) duty at ANOTHER
    ///    location the same day, the shared ShiftEntries row is kept;
    ///  - otherwise the agent is left with nothing on that day and is reported so RTM
    ///    knows to add a BO shift.
    /// </summary>
    public async Task<CleanupClosedDaysResponseDto> CleanupClosedDayDutiesAsync(
        string locationCode, DateOnly effectiveFrom)
    {
        var today  = DateOnly.FromDateTime(DateTime.Today);
        var cutoff = effectiveFrom > today ? effectiveFrom : today;

        var location = await _db.WicLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode)
            ?? throw new KeyNotFoundException($"Location '{locationCode}' not found.");

        var allHours = await _db.WicOpeningHours.ToListAsync();

        var candidates = await _db.WicShiftEntries
            .Where(w => w.IsOnSite && w.ShiftDate >= cutoff &&
                (w.LocationCode == locationCode || w.SupportLocation == location.DisplayName))
            .ToListAsync();

        var toDelete = candidates.Where(w =>
        {
            var hours = WicHoursResolver.Resolve(allHours, location.LocationCode,
                location.LocationCodeLegacy, (int)w.ShiftDate.DayOfWeek, w.ShiftDate);
            return hours is { IsClosed: true };
        }).ToList();

        var agentDays = toDelete.Select(w => (w.EmployeeId, w.ShiftDate)).Distinct().ToList();
        var deleteIds = toDelete.Select(w => w.Id).ToHashSet();

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            _db.WicShiftEntries.RemoveRange(toDelete);

            var leftWithNothing     = new List<CleanupAgentDayDto>();
            var splitKept           = new List<CleanupAgentDayDto>();
            var shiftEntriesRemoved = 0;

            foreach (var (empId, date) in agentDays)
            {
                // Live duty at another location on the same day? Then the shared
                // ShiftEntries WIC_DUTY row must survive (split-duty guard).
                var hasOtherDuty = await _db.WicShiftEntries
                    .AnyAsync(w => w.EmployeeId == empId && w.ShiftDate == date
                                && w.IsOnSite && !deleteIds.Contains(w.Id));

                // Absence safety: only WIC_DUTY rows are ever removed here. AL/SL/OFF/PH/
                // TRAINING etc. are never touched.
                var dutyRows = await _db.ShiftEntries
                    .Where(s => s.EmployeeId == empId && s.ShiftDate == date
                             && s.ShiftType == ShiftTypes.WicDuty)
                    .ToListAsync();

                var name = await _db.Employees.Where(e => e.EmployeeId == empId)
                    .Select(e => e.FullName).FirstOrDefaultAsync();
                var dto = new CleanupAgentDayDto(empId, name, date.ToString("yyyy-MM-dd"),
                    date.DayOfWeek.ToString()[..3]);

                if (hasOtherDuty)
                {
                    splitKept.Add(dto);
                    continue;
                }

                if (dutyRows.Count > 0)
                {
                    _db.ShiftEntries.RemoveRange(dutyRows);
                    shiftEntriesRemoved += dutyRows.Count;
                    leftWithNothing.Add(dto);
                }
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _log.LogInformation(
                "CleanupClosedDayDuties {Location}: removed {Wic} WicShiftEntries + {Shift} ShiftEntries (cutoff {Cutoff})",
                locationCode, toDelete.Count, shiftEntriesRemoved, cutoff);

            return new CleanupClosedDaysResponseDto(
                true, locationCode, cutoff.ToString("yyyy-MM-dd"),
                toDelete.Count, shiftEntriesRemoved,
                leftWithNothing.OrderBy(x => x.Date).ToList(),
                splitKept.OrderBy(x => x.Date).ToList());
        });
    }
}

public static class WicScheduleEndpointMapper
{
    public static void MapWicScheduleEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/wicschedule").WithTags("WicSchedule");

        grp.MapGet("/agents", async (string? from, string? to, string? employeeId, WicScheduleService svc) =>
            Results.Ok(await svc.GetAgentScheduleAsync(
                from ?? DateTime.Today.ToString("yyyy-MM-dd"),
                to   ?? DateTime.Today.AddDays(13).ToString("yyyy-MM-dd"), employeeId)));

        grp.MapGet("/opening-hours", async (WicScheduleService svc) =>
            Results.Ok(await svc.GetOpeningHoursAsync()));

        grp.MapPatch("/opening-hours/{locationCode}/{dow:int}/min-required",
            async (string locationCode, int dow, MinRequiredDto body, GSDDashboard.API.Data.GSDContext db) =>
        {
            var rows = await db.WicOpeningHours
                .Where(h => h.LocationCode == locationCode && h.DayOfWeek == dow)
                .ToListAsync();
            if (rows.Count == 0)
                return Results.NotFound(new { error = $"No opening-hours row for {locationCode} DOW={dow}" });
            foreach (var r in rows) r.MinRequired = body.Value;
            await db.SaveChangesAsync();
            return Results.Ok(new { locationCode, dow, minRequired = body.Value });
        });

        // ── Schedule version editor ──────────────────────────────────────────────
        grp.MapPost("/opening-hours/{locationCode}/version",
            async (string locationCode, UpdateScheduleRequestDto req, GSDDashboard.API.Data.GSDContext db) =>
        {
            if (!DateOnly.TryParse(req.EffectiveFrom, out var effectiveDate))
                return Results.BadRequest(new { error = "Invalid effectiveFrom date." });

            var location = await db.WicLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode);
            if (location == null)
                return Results.NotFound(new { error = $"Location '{locationCode}' not found." });

            // CloseEntireCentre: override every day to closed
            var days = req.CloseEntireCentre
                ? Enumerable.Range(0, 7).Select(i => new UpdateScheduleDayDto(i, true)).ToList()
                : req.Days ?? new();

            if (days.Count != 7 || days.Select(d => d.DayOfWeek).Distinct().Count() != 7)
                return Results.BadRequest(new { error = "Must provide exactly 7 entries, one per DayOfWeek (0=Sun … 6=Sat)." });

            // Validate open-day time windows
            foreach (var d in days.Where(d => !d.IsClosed))
            {
                if (string.IsNullOrWhiteSpace(d.OpenTime) || string.IsNullOrWhiteSpace(d.CloseTime))
                    return Results.BadRequest(new { error = $"DayOfWeek {d.DayOfWeek}: open and close times required for an open day." });
                if (!TimeSpan.TryParse(d.OpenTime, out var ts1) || !TimeSpan.TryParse(d.CloseTime, out var ts2))
                    return Results.BadRequest(new { error = $"DayOfWeek {d.DayOfWeek}: invalid time format (use HH:MM)." });
                if (ts2 <= ts1)
                    return Results.BadRequest(new { error = $"DayOfWeek {d.DayOfWeek}: close time must be after open time." });
                if (!string.IsNullOrWhiteSpace(d.OpenTime2) || !string.IsNullOrWhiteSpace(d.CloseTime2))
                {
                    if (string.IsNullOrWhiteSpace(d.OpenTime2) || string.IsNullOrWhiteSpace(d.CloseTime2))
                        return Results.BadRequest(new { error = $"DayOfWeek {d.DayOfWeek}: both start and end required for second window." });
                    if (!TimeSpan.TryParse(d.OpenTime2, out var ts3) || !TimeSpan.TryParse(d.CloseTime2, out var ts4))
                        return Results.BadRequest(new { error = $"DayOfWeek {d.DayOfWeek}: invalid time format in second window." });
                    if (ts3 <= ts2)
                        return Results.BadRequest(new { error = $"DayOfWeek {d.DayOfWeek}: second window must start after first window ends." });
                    if (ts4 <= ts3)
                        return Results.BadRequest(new { error = $"DayOfWeek {d.DayOfWeek}: second window close must be after second window open." });
                }
            }

            // Carry MinRequired forward from the latest version effective on or before the NEW EffectiveFrom.
            // Using effectiveDate (not today) means two future versions saved in sequence each inherit
            // from the previous future version, not from the currently-live schedule.
            var allHours = await db.WicOpeningHours
                .Where(h => h.LocationCode == locationCode)
                .ToListAsync();
            var currentMinRequired = allHours
                .Where(h => h.EffectiveFrom == null || h.EffectiveFrom <= effectiveDate)
                .GroupBy(h => h.DayOfWeek)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(h => h.EffectiveFrom ?? DateOnly.MinValue).First().MinRequired);

            static string BuildRaw(UpdateScheduleDayDto d)
            {
                if (d.IsClosed) return "Closed";
                if (!string.IsNullOrWhiteSpace(d.OpenTime2) && !string.IsNullOrWhiteSpace(d.CloseTime2))
                    return $"{d.OpenTime} - {d.CloseTime} / {d.OpenTime2} - {d.CloseTime2}";
                return $"{d.OpenTime} - {d.CloseTime}";
            }

            var note = req.ChangeNote;
            var newRows = days.Select(d => new WicOpeningHour
            {
                LocationCode  = locationCode,
                DayOfWeek     = d.DayOfWeek,
                OpenTime      = d.IsClosed ? null : d.OpenTime,
                CloseTime     = d.IsClosed ? null : d.CloseTime,
                OpenTime2     = (d.IsClosed || string.IsNullOrWhiteSpace(d.OpenTime2)) ? null : d.OpenTime2,
                CloseTime2    = (d.IsClosed || string.IsNullOrWhiteSpace(d.CloseTime2)) ? null : d.CloseTime2,
                IsClosed      = d.IsClosed,
                RawSchedule   = BuildRaw(d),
                EffectiveFrom = effectiveDate,
                ChangeNote    = note,
                MinRequired   = currentMinRequired.TryGetValue(d.DayOfWeek, out var mr) ? mr : null,
            }).ToList();

            db.WicOpeningHours.AddRange(newRows);
            await db.SaveChangesAsync();

            // Compute consequences: WicShiftEntries for this location on/after effectiveFrom
            var scheduleByDow = days.ToDictionary(d => d.DayOfWeek);
            var futureEntries = await db.WicShiftEntries
                .Where(w => w.SupportLocation == location.DisplayName && w.ShiftDate >= effectiveDate)
                .Join(db.Employees, w => w.EmployeeId, e => e.EmployeeId,
                      (w, e) => new { w, e.FullName })
                .ToListAsync();

            var consequences = new List<ScheduleConsequenceDto>();
            foreach (var item in futureEntries)
            {
                var dow = (int)item.w.ShiftDate.DayOfWeek;
                if (!scheduleByDow.TryGetValue(dow, out var daySchedule)) continue;

                string? issue = null;
                if (daySchedule.IsClosed)
                {
                    issue = "CLOSED_DAY";
                }
                else if (!string.IsNullOrEmpty(item.w.WorkingShift) && !string.IsNullOrEmpty(daySchedule.OpenTime))
                {
                    var parts = item.w.WorkingShift.Split('-');
                    if (parts.Length >= 2 &&
                        TimeSpan.TryParse(parts[0].Trim(), out var shiftStart) &&
                        TimeSpan.TryParse(parts[1].Trim(), out var shiftEnd) &&
                        TimeSpan.TryParse(daySchedule.OpenTime, out var openTs) &&
                        TimeSpan.TryParse(daySchedule.CloseTime, out var closeTs))
                    {
                        bool withinW1 = shiftStart >= openTs && shiftEnd <= closeTs;
                        bool withinW2 = !string.IsNullOrEmpty(daySchedule.OpenTime2)
                                     && !string.IsNullOrEmpty(daySchedule.CloseTime2)
                                     && TimeSpan.TryParse(daySchedule.OpenTime2, out var openTs2)
                                     && TimeSpan.TryParse(daySchedule.CloseTime2, out var closeTs2)
                                     && shiftStart >= openTs2 && shiftEnd <= closeTs2;
                        if (!withinW1 && !withinW2)
                            issue = "OUTSIDE_WINDOW";
                    }
                }

                if (issue != null)
                    consequences.Add(new ScheduleConsequenceDto(
                        item.w.EmployeeId, item.FullName,
                        item.w.ShiftDate.ToString("yyyy-MM-dd"),
                        item.w.ShiftDate.DayOfWeek.ToString()[..3],
                        item.w.SupportLocation, item.w.WorkingShift, issue));
            }

            return Results.Ok(new UpdateScheduleResponseDto(
                true, req.EffectiveFrom, newRows.Count, consequences));
        });

        // Read-only check: future WIC duties that fall on a day their location is closed.
        grp.MapGet("/closed-day-duties", async (string? locationCode, WicScheduleService svc) =>
            Results.Ok(await svc.GetClosedDayDutiesAsync(locationCode)));

        // Confirmed cleanup: remove future duties on now-closed days for one location.
        // Called from the Opening Hours editor AFTER the user has seen the consequence list.
        grp.MapPost("/opening-hours/{locationCode}/cleanup-closed-days",
            async (string locationCode, CleanupClosedDaysRequestDto req, WicScheduleService svc) =>
        {
            if (!DateOnly.TryParse(req.EffectiveFrom, out var effectiveFrom))
                return Results.BadRequest(new { error = "Invalid effectiveFrom date." });
            try
            {
                return Results.Ok(await svc.CleanupClosedDayDutiesAsync(locationCode, effectiveFrom));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        grp.MapGet("/export/agents/csv", async (string? from, string? to, WicScheduleService svc, HttpContext ctx) =>
        {
            var f = from ?? DateTime.Today.ToString("yyyy-MM-dd");
            var t = to   ?? DateTime.Today.AddDays(13).ToString("yyyy-MM-dd");
            var csv = await svc.ExportAgentsCsvAsync(f, t);
            var bytes = System.Text.Encoding.UTF8.GetBytes("\uFEFF" + csv);
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"WIC_Schedule_{f}_{t}.csv\"";
            return Results.File(bytes, "text/csv; charset=utf-8");
        });
    }
}
