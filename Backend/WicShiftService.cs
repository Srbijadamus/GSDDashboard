using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.WicShifts;

public record WicShiftDto(
    int Id, string EmployeeId, string? FullName, string? TeamLeadName,
    string ShiftDate, string? DayOfWeek, string? SupportLocation,
    string? WicOpeningHours, string? WorkingShift,
    bool IsOnSite, bool IsGSDDay, bool IsOffDay, string? Task, string? AgentStatus
);

public record WicCoverageDto(
    string LocationCode, string DisplayName, string City, string Country,
    string? OpeningSchedule, bool IsCovered, int AgentCount,
    List<WicAgentInfo> Agents
);

public record WicAgentInfo(string EmployeeId, string? Name, string? TeamLead, string? WorkingShift, string? WicOpeningHours);

public record AvailableHoursDto(
    string Date, string EmployeeId, string? Name, string? TeamLead, string? Location,
    string? WicOpenTime, string? WicCloseTime, string? AgentStartTime, string? AgentEndTime,
    double FreeHours, string Status
);

public record PatchWicShiftRequest(string? Task, string? SupportLocation, bool? IsOnSite);

public record CreateShiftRequest(
    string EmployeeId,
    string Date,
    string ShiftType,
    string? ShiftStart,
    string? ShiftEnd,
    string? AgentTask,
    string? LocationCode
);

// ConfirmManualReplace is the explicit second step of the "the existing row for this
// day is a human's decision" case: the first call returns requiresConfirmation=true with
// who set it and when; the UI asks the RTM and re-sends the same day with this flag, which
// is the human's confirmation to replace that row. It never bypasses anything else and an
// automatic caller (manualActor == null) is unaffected by it.
public record CreateAssignmentRequest(
    string EmployeeId,
    string LocationCode,
    string Date,
    string? ShiftStart,
    string? ShiftEnd,
    bool ConfirmManualReplace = false,
    // "Assign anyway" (Overview v2 manual add): a human (manualActor != null) explicitly
    // overrides a blocking absence (AL/SL/OFF/PH/TRAINING/VWIC) so the WIC duty is written
    // anyway. Automatic writers never pass this and stay blocked.
    bool AssignAnyway = false
);

// DateTo/SkipWeekends optional (default: DateTo=Date, SkipWeekends=true) so single-day callers
// (WIC Schedule kebab menu, WIC Attendance agent-chip icon) are unaffected; the "Assign Agent to
// Location" dialog's date-range picker passes DateTo/SkipWeekends to move every day in the range.
public record MoveToGsdRequest(string EmployeeId, string Date, string? DateTo = null, bool SkipWeekends = true);
public record MoveToGsdDayResult(string Date, bool Moved, string? Reason);

public record WicOpenIntervalDto(string OpenTime, string CloseTime);

public record WicDayStatusDto(
    string LocationCode, string DisplayName, string City, string Country,
    bool IsOpen, string? ClosedReason,
    List<WicOpenIntervalDto> OpenIntervals,
    string CoverageStatus,
    int ScheduledCount, int AbsentCount, int EffectiveCoverage,
    int? MinRequired
);

public record WicOpenDayDto(string Date, string DayOfWeek, List<WicDayStatusDto> Locations);

public class WicShiftService
{
    private readonly GSDContext _db;
    private readonly AvailabilityResolver _resolver;
    public WicShiftService(GSDContext db, AvailabilityResolver resolver) { _db = db; _resolver = resolver; }

    public async Task<List<WicShiftDto>> GetWicShiftsAsync(
        string? from, string? to, string? location, string? employeeId, string? teamLead)
    {
        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : fromDate;

        var q = _db.WicShiftEntries
            .Where(w => w.ShiftDate >= fromDate && w.ShiftDate <= toDate)
            .Join(_db.Employees.Where(e => e.IsActive), w => w.EmployeeId, e => e.EmployeeId,
                  (w, e) => new { Wic = w, Emp = e });

        if (!string.IsNullOrWhiteSpace(location))
            q = q.Where(x => x.Wic.SupportLocation == location);
        if (!string.IsNullOrWhiteSpace(employeeId))
            q = q.Where(x => x.Wic.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(teamLead))
            q = q.Where(x => x.Emp.TeamLeadName == teamLead);

        var rows = await q.OrderBy(x => x.Wic.ShiftDate).ThenBy(x => x.Emp.FullName).ToListAsync();

        var empIds = rows.Select(r => r.Wic.EmployeeId).Distinct().ToList();
        var shiftStatuses = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= fromDate && s.ShiftDate <= toDate && empIds.Contains(s.EmployeeId))
            .Select(s => new { s.EmployeeId, s.ShiftDate, s.ShiftType, s.Id })
            .ToListAsync();

        // GroupBy guards against duplicate (EmployeeId, ShiftDate) rows; highest Id wins.
        var statusMap = shiftStatuses
            .GroupBy(s => s.EmployeeId + "_" + s.ShiftDate.ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Id).First().ShiftType);

        return rows.Select(x => {
            var key = x.Wic.EmployeeId + "_" + x.Wic.ShiftDate.ToString("yyyy-MM-dd");
            var agentStatus = statusMap.TryGetValue(key, out var st) ? st : null;
            return new WicShiftDto(
                x.Wic.Id, x.Wic.EmployeeId,
                x.Emp.FullName ?? x.Wic.EmployeeId,
                x.Emp.TeamLeadName,
                x.Wic.ShiftDate.ToString("yyyy-MM-dd"),
                x.Wic.DayOfWeek, x.Wic.SupportLocation,
                x.Wic.WicOpeningHours, x.Wic.WorkingShift,
                x.Wic.IsOnSite, x.Wic.IsGSDDay, x.Wic.IsOffDay,
                x.Wic.Task ?? "WIC", agentStatus
            );
        }).ToList();
    }

    public async Task<WicShiftDto?> PatchWicShiftAsync(int id, PatchWicShiftRequest req)
    {
        var entry = await _db.WicShiftEntries.FindAsync(id);
        if (entry == null) return null;

        if (req.Task != null) entry.Task = req.Task;
        if (req.SupportLocation != null) entry.SupportLocation = req.SupportLocation == "" ? null : req.SupportLocation;
        if (req.IsOnSite != null) entry.IsOnSite = req.IsOnSite.Value;

        if (req.Task == "Voice" || req.Task == "Backlog")
        {
            entry.SupportLocation = null;
            entry.IsOnSite = false;
        }
        var specialStatuses = new[] { "SL", "AL", "Training", "OFF", "GSD" };
        if (specialStatuses.Contains(req.Task) && req.SupportLocation != null)
        {
            entry.IsOnSite = true;
        }

        if (req.Task == "WIC" && !string.IsNullOrEmpty(req.SupportLocation))
            entry.IsOnSite = true;

        await _db.SaveChangesAsync();

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == entry.EmployeeId);
        return new WicShiftDto(
            entry.Id, entry.EmployeeId,
            emp?.FullName ?? entry.EmployeeId,
            emp?.TeamLeadName,
            entry.ShiftDate.ToString("yyyy-MM-dd"),
            entry.DayOfWeek, entry.SupportLocation,
            entry.WicOpeningHours, entry.WorkingShift,
            entry.IsOnSite, entry.IsGSDDay, entry.IsOffDay,
            entry.Task ?? "WIC", null
        );
    }

    public async Task<List<object>> GetLocationsAsync()
    {
        var locations = await _db.WicLocations.Where(l => l.IsActive)
            .OrderBy(l => l.Country).ThenBy(l => l.City).ToListAsync();
        var hours = await _db.WicOpeningHours.ToListAsync();
        var hoursByCode = hours.GroupBy(h => h.LocationCode)
            .ToDictionary(g => g.Key, g => g.ToList());

        string[] dayAbbr = { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };

        string BuildSchedule(string code, string? legacyCode = null)
        {
            if (!hoursByCode.TryGetValue(code, out var list) &&
                (legacyCode == null || !hoursByCode.TryGetValue(legacyCode, out list)))
                return "";
            var today = DateOnly.FromDateTime(DateTime.Today);
            int[] order = { 1, 2, 3, 4, 5, 6, 0 };
            var parts = new List<string>();
            foreach (var dow in order)
            {
                var h = list
                    .Where(x => x.DayOfWeek == dow && (x.EffectiveFrom == null || x.EffectiveFrom <= today))
                    .OrderByDescending(x => x.EffectiveFrom ?? DateOnly.MinValue)
                    .FirstOrDefault();
                if (h == null) continue;
                if (h.IsClosed) { parts.Add($"{dayAbbr[dow]} Closed"); continue; }
                if (string.IsNullOrEmpty(h.OpenTime) && string.IsNullOrEmpty(h.CloseTime)) continue;
                var seg = $"{h.OpenTime}-{h.CloseTime}";
                if (!string.IsNullOrEmpty(h.OpenTime2))
                    seg += $", {h.OpenTime2}-{h.CloseTime2}";
                parts.Add($"{dayAbbr[dow]} {seg}");
            }
            return string.Join(" | ", parts);
        }

        string BuildAddress(string code)
        {
            var p = code.Split('~');
            return p.Length >= 4 ? p[3].Trim() : "";
        }

        return locations.Select(l => (object)new {
            l.Id,
            l.LocationCode,
            l.DisplayName,
            l.City,
            l.Country,
            l.IsActive,
            l.IsNpp,
            l.Coordinates,
            openingSchedule = BuildSchedule(l.LocationCode, l.LocationCodeLegacy),
            fullAddress = BuildAddress(l.LocationCode)
        }).ToList();
    }

    public async Task<List<WicOpenDayDto>> GetOpenAsync(string? dateStr, int horizon = 3)
    {
        horizon = Math.Clamp(horizon, 1, 7);
        var startDate = dateStr != null && DateOnly.TryParse(dateStr, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
        var endDate   = startDate.AddDays(horizon - 1);

        var locations      = await _db.WicLocations.Where(l => l.IsActive).OrderBy(l => l.Country).ThenBy(l => l.City).ToListAsync();
        var allHours       = await _db.WicOpeningHours.ToListAsync();
        var publicHolidays = await _db.PublicHolidays.ToListAsync();

        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= startDate && w.ShiftDate <= endDate)
            .Join(_db.Employees.Where(e => e.IsActive), w => w.EmployeeId, e => e.EmployeeId, (w, e) => w)
            .ToListAsync();
        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));

        var result = new List<WicOpenDayDto>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            int dow = (int)date.DayOfWeek; // 0=Sun ... 6=Sat

            bool isNationalHoliday = publicHolidays.Any(ph => ph.HolidayDate == date && ph.IsNational);

            var dayWicIds = wicEntries
                .Where(w => w.ShiftDate == date && w.IsOnSite)
                .Select(w => w.EmployeeId).Distinct().ToList();
            var absentToday = await _resolver.GetAbsentIdsAsync(dayWicIds, date);

            var dayLocations = locations.Select(loc =>
            {
                // Use the EffectiveFrom-aware resolver so future-dated schedule versions
                // are not picked up early (was plain FirstOrDefault over all rows).
                var hours = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy, dow, date);

                string? bundesland = loc.Bundesland
                    ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country);
                bool isRegionalHoliday = bundesland != null &&
                    publicHolidays.Any(ph => ph.HolidayDate == date &&
                        string.Equals(ph.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase));

                bool isClosed = hours == null || hours.IsClosed || isNationalHoliday || isRegionalHoliday;
                string? closedReason = isClosed
                    ? (isNationalHoliday || isRegionalHoliday ? "PUBLIC_HOLIDAY" : "CLOSED_DAY")
                    : null;

                var intervals = new List<WicOpenIntervalDto>();
                if (!isClosed && hours != null)
                {
                    intervals.Add(new WicOpenIntervalDto(hours.OpenTime ?? "", hours.CloseTime ?? ""));
                    if (!string.IsNullOrEmpty(hours.OpenTime2))
                        intervals.Add(new WicOpenIntervalDto(hours.OpenTime2, hours.CloseTime2 ?? ""));
                }

                var dayWic = wicEntries
                    .Where(w => w.ShiftDate == date && w.IsOnSite && WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc))
                    .ToList();

                int fullAbsentCount = 0;
                double presentDouble = 0;
                foreach (var w in dayWic)
                {
                    if (absentToday.Contains(w.EmployeeId))
                    {
                        fullAbsentCount++;
                    }
                    else
                    {
                        shiftByEmpDate.TryGetValue((w.EmployeeId, date), out var sh);
                        // Signal 1 (the agent HAS an on-site row here): a HALF_AL day is
                        // the working part of the day, and the on-site row's WorkingShift
                        // is already cut to the overlap of that part and this centre's
                        // window — so he staffs this centre, not "half of himself"
                        // (RTM 2026-10-15, requirement 1).
                        double contrib = AvailabilityResolver.GetOnSiteWicContribution(false, sh);
                        if (contrib <= 0.0) fullAbsentCount++; // non-WIC work also reduces effective count
                        else presentDouble += contrib;
                    }
                }

                int scheduledCount = dayWic.Count;
                int effectiveCoverage = (int)Math.Floor(presentDouble);
                int absentCount = fullAbsentCount;
                // Per-schedule MinRequired takes precedence (matches ForecastService.cs:105);
                // fall back to the location default.
                int minReq = hours?.MinRequired ?? loc.MinAgentsRequired ?? 1;

                string coverageStatus = CoverageEvaluator.Classify(isClosed, effectiveCoverage, minReq, closedReason).Status.ToString();

                return new WicDayStatusDto(
                    loc.LocationCode, loc.DisplayName, loc.City ?? "", loc.Country ?? "DE",
                    !isClosed, closedReason, intervals,
                    coverageStatus, scheduledCount, absentCount, effectiveCoverage,
                    minReq);
            }).ToList();

            result.Add(new WicOpenDayDto(date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(), dayLocations));
        }

        return result;
    }

    public async Task<List<WicCoverageDto>> GetCoverageAsync(DateOnly date)
    {
        var locations = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var entries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date && w.IsOnSite)
            .Join(_db.Employees.Where(e => e.IsActive), w => w.EmployeeId, e => e.EmployeeId, (w, e) => new { w, e })
            .ToListAsync();

        var byLocation = entries.GroupBy(x => x.w.SupportLocation ?? "").ToDictionary(g => g.Key, g => g.ToList());

        return locations.Select(loc => {
            var agents = byLocation.TryGetValue(loc.DisplayName, out var list) ? list : [];
            var agentInfos = agents.Select(x => new WicAgentInfo(
                x.e.EmployeeId, x.e.FullName ?? x.e.EmployeeId,
                x.e.TeamLeadName?.Trim(), x.w.WorkingShift, x.w.WicOpeningHours
            )).ToList();
            return new WicCoverageDto(
                loc.LocationCode, loc.DisplayName, loc.City ?? "", loc.Country ?? "DE",
                loc.OpeningSchedule, agentInfos.Count > 0, agentInfos.Count, agentInfos
            );
        }).ToList();
    }

    public async Task<List<AvailableHoursDto>> GetAvailableHoursAsync(string? from, string? to, string? teamLead)
    {
        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : fromDate;

        var entries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= fromDate && w.ShiftDate <= toDate && w.IsOnSite && !w.IsOffDay)
            .Join(_db.Employees.Where(e => e.IsActive), w => w.EmployeeId, e => e.EmployeeId, (w, e) => new { Wic = w, Emp = e })
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(teamLead))
            entries = entries.Where(x => x.Emp.TeamLeadName == teamLead).ToList();

        var locations = await _db.WicLocations.ToListAsync();
        var openingHours = await _db.WicOpeningHours.ToListAsync();
        var locByName = locations.ToDictionary(l => l.DisplayName, l => l, StringComparer.OrdinalIgnoreCase);

        var result = new List<AvailableHoursDto>();

        foreach (var x in entries)
        {
            var agentShift = x.Wic.WorkingShift;
            if (string.IsNullOrWhiteSpace(agentShift)) continue;

            var agentParts = agentShift.Replace(" ", "").Split('-');
            if (agentParts.Length < 2) continue;
            if (!TimeOnly.TryParse(agentParts[0], out var agentStart) ||
                !TimeOnly.TryParse(agentParts[1], out var agentEnd)) continue;

            var locName = x.Wic.SupportLocation ?? "";
            if (!locByName.TryGetValue(locName, out var loc)) continue;

            // Use .NET DayOfWeek (0=Sun...6=Sat) — same convention as the rest of the codebase
            int dow = (int)x.Wic.ShiftDate.DayOfWeek;

            // Date-specific exception day first (manual exception / Rollout day), then the
            // weekday pattern — same precedence as WicHoursResolver.
            var oh = openingHours.FirstOrDefault(o =>
                        (o.LocationCode == loc.LocationCode ||
                         (loc.LocationCodeLegacy != null && o.LocationCode == loc.LocationCodeLegacy)) &&
                        o.ExceptionDate == x.Wic.ShiftDate)
                  ?? openingHours.FirstOrDefault(o =>
                        (o.LocationCode == loc.LocationCode ||
                         (loc.LocationCodeLegacy != null && o.LocationCode == loc.LocationCodeLegacy)) &&
                        o.ExceptionDate == null && o.DayOfWeek == dow);
            if (oh == null || oh.IsClosed || oh.CloseTime == null) continue;
            if (!TimeOnly.TryParse(oh.OpenTime, out var wicOpen) ||
                !TimeOnly.TryParse(oh.CloseTime, out var wicClose)) continue;

            var agentEndAdj = agentEnd < agentStart ? agentEnd.AddMinutes(24 * 60) : agentEnd;
            var wicCloseAdj = wicClose < wicOpen ? wicClose.AddMinutes(24 * 60) : wicClose;

            string status;
            double freeHours;

            if (agentEndAdj < wicCloseAdj) { status = "shift_too_short"; freeHours = 0; }
            else
            {
                freeHours = (agentEndAdj - wicCloseAdj).TotalHours;
                status = freeHours > 0 ? "available" : "fully_occupied";
            }

            if (status == "fully_occupied") continue;

            result.Add(new AvailableHoursDto(
                x.Wic.ShiftDate.ToString("yyyy-MM-dd"), x.Wic.EmployeeId,
                x.Emp.FullName ?? x.Wic.EmployeeId, x.Emp.TeamLeadName, locName,
                oh.OpenTime, oh.CloseTime, agentParts[0], agentParts[1],
                Math.Round(freeHours, 1), status
            ));
        }

        return result.OrderBy(r => r.Date).ThenByDescending(r => r.FreeHours).ToList();
    }

    public async Task<byte[]> ExportToExcelAsync(string? from, string? to)
    {
        var rows = await GetWicShiftsAsync(from, to, null, null, null);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("WIC Shifts");
        var headers = new[] { "Emp ID", "Name", "Team Lead", "Date", "Day", "Location", "Opening Hours", "Working Shift", "On Site", "GSD Day", "Task", "Status" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0e7490");
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r]; var er = r + 2;
            ws.Cell(er, 1).Value = row.EmployeeId;
            ws.Cell(er, 2).Value = row.FullName ?? "";
            ws.Cell(er, 3).Value = row.TeamLeadName ?? "";
            ws.Cell(er, 4).Value = row.ShiftDate;
            ws.Cell(er, 5).Value = row.DayOfWeek ?? "";
            ws.Cell(er, 6).Value = row.SupportLocation ?? "";
            ws.Cell(er, 7).Value = row.WicOpeningHours ?? "";
            ws.Cell(er, 8).Value = row.WorkingShift ?? "";
            ws.Cell(er, 9).Value = row.IsOnSite ? "Yes" : "No";
            ws.Cell(er, 10).Value = row.IsGSDDay ? "Yes" : "No";
            ws.Cell(er, 11).Value = row.Task ?? "WIC";
            ws.Cell(er, 12).Value = row.AgentStatus ?? "";
            if (row.IsOnSite)
                ws.Row(er).Style.Fill.BackgroundColor = XLColor.FromHtml("#cffafe");
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportAvailableHoursCsvAsync(string? from, string? to, string? teamLead)
    {
        var rows = await GetAvailableHoursAsync(from, to, teamLead);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Date,Employee ID,Name,Team Lead,Location,WIC Hours,Agent Shift,Free Hours,Status");
        foreach (var r in rows)
            sb.AppendLine(r.Date + "," + r.EmployeeId + "," + r.Name + "," + r.TeamLead + "," + r.Location + "," + r.WicOpenTime + "-" + r.WicCloseTime + "," + r.AgentStartTime + "-" + r.AgentEndTime + "," + r.FreeHours + "h," + r.Status);
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Creates or updates a single ShiftEntry (and, for WIC_DUTY, its
    /// WicShiftEntries row) — the generic shift-creation route the "+ New
    /// Shift" modal and the coverage-gaps "Assign" button both post to.
    /// Validated through ShiftValidationService (R1-R7) BEFORE anything is
    /// persisted: a hard block (R1/R2/R3/R5) is refused with nothing saved; a
    /// soft violation (R4 Sunday, R7 public holiday, R6 student hours) is
    /// returned alongside the now-saved shift, never blocking.
    /// </summary>
    public async Task<IResult> CreateShiftAsync(CreateShiftRequest req, GSDContext db, ShiftValidationService validationSvc,
        string? manualActor = null)
    {
        if (string.IsNullOrWhiteSpace(req.EmployeeId))
            return Results.BadRequest(new { error = "EmployeeId is required." });
        if (!DateOnly.TryParse(req.Date, out var date))
            return Results.BadRequest(new { error = "Invalid date format. Expected yyyy-MM-dd." });
        if (string.IsNullOrWhiteSpace(req.ShiftType))
            return Results.BadRequest(new { error = "ShiftType is required." });

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId);
        if (employee == null)
            return Results.NotFound(new { error = $"Employee '{req.EmployeeId}' not found." });

        var shift = await db.ShiftEntries
            .FirstOrDefaultAsync(s => s.EmployeeId == req.EmployeeId && s.ShiftDate == date);
        // manualActor != null means THIS call is itself the human's deliberate edit (the
        // "+ New Shift" / edit-cell UI path) — never refuse a human overwriting their own
        // earlier manual decision. Only an automatic writer (manualActor == null: imports,
        // sync, generator) is refused by PreserveManualShift.
        if (manualActor == null && db.PreserveManualShift(shift, "WIC create shift"))
            return Results.Conflict(new { success = false, saved = false, skipped = true, reasonCode = "PROTECTED_MANUAL", reason = "Manual shift is protected." });
        // RTM 2026-10-15 (requirement 1): a WIC duty placed on a HALF_AL day keeps the
        // row HALF_AL — half annual leave is still on it, and its ShiftStart..ShiftEnd
        // is the WORKING part the WIC duty is placed on. Only the WicShiftEntries row
        // below carries the WIC assignment.
        bool keepHalfAl = shift != null
            && string.Equals(shift.ShiftType, ShiftTypes.HalfAL, StringComparison.OrdinalIgnoreCase)
            && string.Equals(req.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase);
        if (shift != null)
        {
            shift.ShiftType    = keepHalfAl ? ShiftTypes.HalfAL : req.ShiftType;
            // The agent's own times are never dropped by an edit that carries none. The WIC
            // block below then fills a still-empty window from the centre's hours, but it
            // must never turn the centre's hours into the agent's shift (RTM rule 2026-10-14).
            // A HALF_AL working part is never re-timed from the request either.
            if (!keepHalfAl)
            {
                shift.ShiftStart = req.ShiftStart ?? shift.ShiftStart;
                shift.ShiftEnd   = req.ShiftEnd   ?? shift.ShiftEnd;
            }
            shift.AgentTask    = req.AgentTask;
            shift.SourceModule = "MANUAL";
        }
        else
        {
            shift = new ShiftEntry
            {
                EmployeeId   = req.EmployeeId,
                ShiftDate    = date,
                ShiftType    = req.ShiftType,
                ShiftStart   = req.ShiftStart,
                ShiftEnd     = req.ShiftEnd,
                AgentTask    = req.AgentTask,
                SourceSheet  = "MANUAL",
                SourceModule = "MANUAL",
            };
            db.ShiftEntries.Add(shift);
        }
        if (manualActor != null)
            db.RecordManualShiftEdit(shift, manualActor);

        // Validate BEFORE anything is persisted — a hard block leaves nothing
        // saved since SaveChangesAsync is never reached (see
        // ShiftValidationService.ValidateAsync entity overload).
        var validation = await validationSvc.ValidateAsync(shift, req.ShiftType, req.ShiftStart, req.ShiftEnd);
        if (!validation.Valid)
        {
            return Results.Ok(new
            {
                success    = false,
                saved      = false,
                blocked    = true,
                reasonCode = "VALIDATION_BLOCKED",
                violations = validation.Violations,
            });
        }

        if (!string.IsNullOrWhiteSpace(req.LocationCode) && req.ShiftType == ShiftTypes.WicDuty)
        {
            var location = await db.WicLocations
                .FirstOrDefaultAsync(l => l.LocationCode == req.LocationCode && l.IsActive);
            if (location != null)
            {
                int dow = (int)date.DayOfWeek;
                // THE single hours definition: this date's exception day (manual exception /
                // Rollout day) wins over the weekday pattern and the legacy location code is
                // honoured — WicHoursResolver, then DutyWindow for a two-interval break.
                var allHours = await db.WicOpeningHours.ToListAsync();
                var (dutyStart, dutyEnd) = WicWorkingShift.CentreWindow(allHours, location, date);
                string openTime  = dutyStart ?? req.ShiftStart ?? "08:00";
                string closeTime = dutyEnd   ?? req.ShiftEnd   ?? "17:00";

                // Backfill the ShiftEntries row itself, not just WicShiftEntries.
                // Found while verifying the "Assign" button: a caller (like the
                // gap-assign modal) that omits ShiftStart/ShiftEnd — expecting
                // the centre's own hours to fill them in — previously got a
                // WIC_DUTY row with blank times and IsWicDuty still false.
                shift.IsWicDuty = !keepHalfAl;
                if (string.IsNullOrEmpty(shift.ShiftStart)) shift.ShiftStart = openTime;
                if (string.IsNullOrEmpty(shift.ShiftEnd))   shift.ShiftEnd   = closeTime;

                // THE RULE (RTM 2026-10-14): the on-site WorkingShift is the agent's window
                // cut to the centre's window. The agent's ShiftEntries times above are only
                // backfilled when empty; they are never replaced by the centre's hours here.
                var workingShift = WicWorkingShift.Slice(shift.ShiftStart, shift.ShiftEnd, dutyStart, dutyEnd)
                                   // No overlap: the row belongs to THIS centre, so it carries
                                   // the centre's hours — not the agent's daily shift (RTM
                                   // 2026-10-15 item 4).
                                   ?? WicWorkingShift.FallbackWindow(dutyStart, dutyEnd)
                                   ?? $"{openTime}-{closeTime}";

                // Keyed by LOCATION, not only by agent+day. WicShiftEntries is unique on
                // (EmployeeId, ShiftDate, SupportLocation) — UQ_WicShift_EmpDateLoc — so an
                // agent may legitimately hold one row per centre per day. Looking the row up
                // without the location made a SECOND same-day assignment MOVE the first
                // centre's row instead of adding to it: the Demmin dual-centre report, where
                // Am Hanseufer flipped to "not on shift" because its only row had been
                // re-pointed at Woldeforster Str. Non-overlapping times are allowed by design
                // (the overlap guard lives in CreateAssignmentAsync; the conflict detector
                // reports real overlaps).
                var wicShift = await db.WicShiftEntries
                    .FirstOrDefaultAsync(w => w.EmployeeId == req.EmployeeId && w.ShiftDate == date
                                           && w.SupportLocation == location.DisplayName);
                if (wicShift != null)
                {
                    wicShift.LocationCode    = req.LocationCode;
                    wicShift.IsOnSite        = true;
                    wicShift.IsOffDay        = false;
                    wicShift.IsGSDDay        = false;
                    wicShift.WorkingShift    = workingShift;
                    wicShift.Task            = "WIC";
                }
                else
                {
                    db.WicShiftEntries.Add(new WicShiftEntry
                    {
                        EmployeeId      = req.EmployeeId,
                        ShiftDate       = date,
                        DayOfWeek       = date.DayOfWeek.ToString(),
                        SupportLocation = location.DisplayName,
                        LocationCode    = req.LocationCode,
                        IsOnSite        = true,
                        IsGSDDay        = false,
                        IsOffDay        = false,
                        WorkingShift    = workingShift,
                        Task            = "WIC",
                    });
                }

                // THE MULTI-CENTRE RULE (RTM 2026-10-15): 2+ on-site centres on this date
                // means the daily ShiftEntries row must span them all. The OTHER centres'
                // rows are read from the database (the current location's row is replaced
                // by the freshly computed `workingShift` so the tracked, not-yet-saved value
                // is never read back stale), and the row is only ever widened.
                var otherParts = await db.WicShiftEntries
                    .Where(w => w.EmployeeId == req.EmployeeId && w.ShiftDate == date && w.IsOnSite
                             && w.SupportLocation != location.DisplayName)
                    .Select(w => w.WorkingShift)
                    .ToListAsync();
                if (WicWorkingShift.WidenDaily(shift.ShiftStart, shift.ShiftEnd,
                        otherParts.Append(workingShift)) is { } widened)
                {
                    shift.ShiftStart = widened.Start;
                    shift.ShiftEnd   = widened.End;
                }
            }
        }

        await db.SaveChangesAsync();
        return Results.Ok(new
        {
            success      = true,
            saved        = true,
            reasonCode   = "SAVED",
            employeeName = employee.FullName ?? req.EmployeeId,
            shiftDate    = date.ToString("yyyy-MM-dd"),
            shiftType    = req.ShiftType,
            violations   = validation.Violations, // soft warnings only — shift is already saved
        });
    }

    /// <summary>
    /// sourceModule/sourceId let a caller other than the interactive "assign"
    /// click (e.g. RosterService's MAIN/BACKUP walk) tag the rows it creates so
    /// they belong to an undoable RosterBatch, same as any other generated row.
    /// Defaults preserve the existing manual-assignment behaviour exactly.
    /// </summary>
    public async Task<IResult> CreateAssignmentAsync(CreateAssignmentRequest req, GSDContext db,
        string sourceModule = "ASSIGN", int? sourceId = null, string? manualActor = null)
    {
        if (string.IsNullOrWhiteSpace(req.EmployeeId) || string.IsNullOrWhiteSpace(req.LocationCode))
            return Results.BadRequest(new { error = "EmployeeId and LocationCode are required." });
        if (!DateOnly.TryParse(req.Date, out var date))
            return Results.BadRequest(new { error = "Invalid date format. Expected yyyy-MM-dd." });

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId);
        if (employee == null)
            return Results.NotFound(new { error = $"Employee '{req.EmployeeId}' not found." });

        var location = await db.WicLocations
            .FirstOrDefaultAsync(l => l.LocationCode == req.LocationCode && l.IsActive);
        if (location == null)
            return Results.NotFound(new { error = $"Location '{req.LocationCode}' not found." });

        string? nppWarning = (location.IsNpp && !employee.NppQualified)
            ? $"{employee.FullName ?? req.EmployeeId} is not NPP-qualified for NPP site {location.DisplayName}."
            : null;

        int dow = (int)date.DayOfWeek;
        var allHours = await db.WicOpeningHours.ToListAsync();
        // Legacy location code included, so a legacy-coded centre resolves its hours too.
        var hours    = WicHoursResolver.Resolve(allHours, location.LocationCode, location.LocationCodeLegacy, dow, date);
        if (hours?.IsClosed == true)
            return Results.Ok(new
            {
                success    = true,
                saved      = false,
                skipped    = true,
                reasonCode = "CLOSED",
                reason     = $"WIC location is closed on {date:yyyy-MM-dd}",
                date       = date.ToString("yyyy-MM-dd"),
            });

        // The agent's own ShiftEntries row for this date (if any). Its ShiftStart/ShiftEnd
        // is the agent's real working window and — when RTM leaves the time fields empty —
        // must never be replaced by the centre's hours (the same rule RosterService applies
        // to its WIC writer since 2026-10-13). The WIC row then gets the slice of that
        // window which falls inside the centre's window.
        var shift = await db.ShiftEntries
            .FirstOrDefaultAsync(s => s.EmployeeId == req.EmployeeId && s.ShiftDate == date);

        // RTM 2026-10-15 (requirement 1): a HALF_AL day is half annual leave and half WORK.
        // A WIC duty may be placed on the WORKING part, so the row must keep saying HALF_AL
        // — its ShiftStart..ShiftEnd is that working part, which is what the HR export
        // ("half AL + half work") and the Shift Plan show. Only the WicShiftEntries row
        // carries the WIC assignment, and its WorkingShift is the intersection of the
        // working part and the centre's window (WicWorkingShift.Slice below).
        // Full AL/SL/UL/PH/LPH/OL/OFF/OFF_WEEKEND/RESIGNED days stay blocked (BlockingAbsenceTypes).
        bool halfAlDay = string.Equals(shift?.ShiftType, ShiftTypes.HalfAL, StringComparison.OrdinalIgnoreCase);

        static (TimeOnly s, TimeOnly e)? ParseWindow(string? start, string? end) =>
            TimeOnly.TryParse(start, out var s) && TimeOnly.TryParse(end, out var e) && e > s
                ? (s, e) : null;
        static (TimeOnly s, TimeOnly e)? Intersect((TimeOnly s, TimeOnly e) a, (TimeOnly s, TimeOnly e) b)
        {
            var s = a.s > b.s ? a.s : b.s;
            var e = a.e < b.e ? a.e : b.e;
            return e > s ? (s, e) : null;
        }
        var agentWindow = ParseWindow(shift?.ShiftStart, shift?.ShiftEnd);
        // A HALF_AL day ALWAYS keeps its own times: they are the working part, never the
        // centre's hours and never the request's WIC times. Without usable times there is
        // nothing to keep, so the WIC row falls back to the centre's window below.
        bool keepAgentShift = agentWindow != null &&
            (halfAlDay || (req.ShiftStart == null && req.ShiftEnd == null));

        // Time source priority for NEW assignments (fixes the Helmstedt/Rendsburg pattern
        // where the ShiftEntry got the location opening hours instead of the agent's time):
        //   1. explicit request time (req.ShiftStart/ShiftEnd)
        //   2. the agent's own ShiftEntries window for this date, kept as-is (keepAgentShift)
        //   3. the agent's existing WorkingShift on WicShiftEntries for this date
        //      (e.g. imported "09:00-17:00" for Helmstedt)
        //   4. the resolved opening hours FOR THIS DATE (WicHoursResolver)
        // Handles single-block ("09:00-17:00") and two-block ("07:00-12:00 / 12:30-13:30")
        // values: the working window spans the first block's start through the last
        // block's end. Anything with fewer than two HH:MM tokens is "unknown".
        static (string? start, string? end) SplitWorkingShift(string? ws)
        {
            if (string.IsNullOrWhiteSpace(ws)) return (null, null);
            var times = System.Text.RegularExpressions.Regex.Matches(ws, @"\d{1,2}:\d{2}");
            return times.Count >= 2 ? (times[0].Value, times[^1].Value) : (null, null);
        }
        // Same location, OR an imported/historical row that was never a live
        // on-site commitment (IsOnSite=false — e.g. the Helmstedt "Global
        // Service Desk" import, which SHOULD still inform the time; see
        // CreateAssignmentTimePriorityTests). Bug found while verifying the
        // Demmin dual-centre case: without this, an ACTIVE on-site row at a
        // DIFFERENT live centre (IsOnSite=true) was picked up too, reusing its
        // time for a second, unrelated location's assignment — which then
        // trivially "overlapped" itself in the conflict check below and
        // silently blocked the second centre.
        var priorWicShift = await db.WicShiftEntries
            .Where(w => w.EmployeeId == req.EmployeeId && w.ShiftDate == date &&
                        (w.SupportLocation == location.DisplayName || !w.IsOnSite))
            .Select(w => w.WorkingShift)
            .FirstOrDefaultAsync(ws => ws != null);
        var (priorStart, priorEnd) = SplitWorkingShift(priorWicShift);

        // Priority order is request > the agent's own ShiftEntries window > the agent's
        // prior WorkingShift > centre hours. The centre-hours fallback spans a two-interval
        // break, matching what SplitWorkingShift already does to a two-block WorkingShift
        // string above.
        var (dutyStart, dutyEnd) = WicHoursResolver.DutyWindow(hours);
        var centreWindow = ParseWindow(dutyStart, dutyEnd);
        string openTime;
        string closeTime;
        if (keepAgentShift)
        {
            // The WIC row is the intersection of the agent's shift and the centre's window.
            // An empty intersection (or a centre row with no usable window) falls back to
            // the agent's own shift — the centre's hours never become the agent's shift.
            var wicWindow = (centreWindow is { } cw ? Intersect(agentWindow!.Value, cw) : null)
                            ?? agentWindow!.Value;
            openTime  = wicWindow.s.ToString("HH:mm");
            closeTime = wicWindow.e.ToString("HH:mm");
        }
        else
        {
            openTime  = req.ShiftStart ?? priorStart ?? dutyStart ?? "08:00";
            closeTime = req.ShiftEnd   ?? priorEnd   ?? dutyEnd   ?? "17:00";
        }

        var agentTask = (location.DisplayName?.Length ?? 0) > 20
            ? location.DisplayName![..20]
            : location.DisplayName ?? req.LocationCode[..Math.Min(20, req.LocationCode.Length)];

        ShiftEntry? newShift = null;
        // manualActor != null means THIS call is itself the human's deliberate "Assign to
        // gap" / "Create Shift" click — never refuse it. Only an automatic writer
        // (manualActor == null: RosterService's MAIN/BACKUP walk, imports, sync) is
        // refused by PreserveManualShift so it can never clobber a human's cell.
        if (manualActor == null && db.PreserveManualShift(shift, "WIC assignment"))
            return Results.Ok(new
            {
                success    = false,
                saved      = false,
                skipped    = true,
                reasonCode = "PROTECTED_MANUAL",
                reason     = "Manual shift is protected.",
                date       = date.ToString("yyyy-MM-dd"),
            });

        // Skip dates where the agent is on a non-working shift; never overwrite AL/SL/OFF/PH
        // etc. (AvailabilityResolver.BlockingAbsenceTypes, shared with WicCardsService), nor
        // TRAINING, nor a WORKING shift explicitly tasked VWIC — same "unavailable" set the
        // permanent WIC-priority rule uses (RosterService.IsProtectedFromOverwrite).
        // A human "Assign anyway" (AssignAnyway=true, manualActor != null) overrides every
        // blocking type EXCEPT sick leave (SL): SL is never overwritten, not even with
        // AssignAnyway — the sick day is untouchable.
        if (shift != null)
        {
            bool isBlocked = AvailabilityResolver.BlockingAbsenceTypes.Contains(shift.ShiftType ?? "") ||
                string.Equals(shift.ShiftType, ShiftTypes.Training, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(shift.ShiftType, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(shift.AgentTask, "VWIC", StringComparison.OrdinalIgnoreCase));
            bool isSick = string.Equals(shift.ShiftType, ShiftTypes.SickLeave, StringComparison.OrdinalIgnoreCase);
            bool overrideAllowed = req.AssignAnyway && manualActor != null;
            if (isBlocked && (isSick || !overrideAllowed))
                return Results.Ok(new
                {
                    success    = true,
                    saved      = false,
                    skipped    = true,
                    reasonCode = "BLOCKING_SHIFT",
                    reason     = $"Agent has shift type '{shift.ShiftType}' on {date:yyyy-MM-dd}",
                    shiftType  = shift.ShiftType,
                    date       = date.ToString("yyyy-MM-dd"),
                });
            // Record the pre-overwrite value in the existing PreviousStatus audit column
            // (ShiftSyncService reverts restore it the same way), so a human AssignAnyway
            // override of a blocking absence can be undone. No schema change.
            if (isBlocked && overrideAllowed)
                shift.PreviousStatus = shift.ShiftType;
        }

        if (shift != null)
        {
            // HALF_AL stays HALF_AL (see halfAlDay above); every other type becomes WIC duty
            // exactly as before.
            if (!halfAlDay)
            {
                shift.ShiftType = ShiftTypes.WicDuty;
                shift.IsWicDuty = true;
            }
            shift.AgentTask = agentTask;
            // keepAgentShift: the agent's own ShiftStart/ShiftEnd stays exactly as his
            // schedule has it. The centre's window only shapes the WIC row above.
            // A HALF_AL row is never re-timed here either — its times ARE the working part.
            if (!keepAgentShift && !halfAlDay)
            {
                shift.ShiftStart = req.ShiftStart ?? openTime;
                shift.ShiftEnd   = req.ShiftEnd   ?? closeTime;
            }
            shift.SourceModule = sourceModule;
            if (sourceId != null) shift.SourceId = sourceId;
        }
        else
        {
            // Deliberately NOT assigned to `shift` — the span-recalculation block below
            // (existingWicShifts.Count > 0 && shift != null) must keep treating a brand
            // new ShiftEntry as untouched, preserving the existing request-time-wins
            // priority (see CreateAssignmentTimePriorityTests). `newShift` is still the
            // row RecordManualShiftEdit tags below.
            newShift = new ShiftEntry
            {
                EmployeeId   = req.EmployeeId,
                ShiftDate    = date,
                ShiftType    = ShiftTypes.WicDuty,
                IsWicDuty    = true,
                AgentTask    = agentTask,
                ShiftStart   = req.ShiftStart ?? openTime,
                ShiftEnd     = req.ShiftEnd   ?? closeTime,
                SourceSheet  = sourceModule,
                SourceModule = sourceModule,
                SourceId     = sourceId,
            };
            db.ShiftEntries.Add(newShift);
        }

        // THE RULE (RTM 2026-10-14): the on-site WorkingShift is the agent's own window
        // cut to this centre's resolved window. The agent's ShiftEntries times above stay
        // exactly as they are — only the WIC row's value is derived from them. This is the
        // universal form of the intersection the keepAgentShift branch only applied when
        // the request carried no times (see WicWorkingShift).
        var agentWicStart = (shift ?? newShift)!.ShiftStart;
        var agentWicEnd   = (shift ?? newShift)!.ShiftEnd;
        var workingShift  = WicWorkingShift.Slice(agentWicStart, agentWicEnd, dutyStart, dutyEnd)
                            // No overlap: this row belongs to THIS centre, so it carries the
                            // centre's own hours — never the agent's daily shift (RTM
                            // 2026-10-15 item 4, the Demmin dual-centre case).
                            ?? WicWorkingShift.FallbackWindow(dutyStart, dutyEnd)
                            ?? $"{openTime}-{closeTime}";

        // Check for time-range overlap with existing WicShiftEntries for this agent+date.
        // Two assignments are allowed on the same day only if their times do not overlap.
        // NULL or unparseable times are treated as "unknown — don't block".
        var existingWicShifts = await db.WicShiftEntries
            .Where(w => w.EmployeeId == req.EmployeeId && w.ShiftDate == date)
            .ToListAsync();

        static (TimeSpan? start, TimeSpan? end) ParseShift(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return (null, null);
            var parts = s.Split('-');
            if (parts.Length < 2) return (null, null);
            return TimeSpan.TryParse(parts[0].Trim(), out var a) && TimeSpan.TryParse(parts[1].Trim(), out var b)
                ? (a, b) : (null, null);
        }
        var (newStart, newEnd) = ParseShift(workingShift);

        // Only IsOnSite=true rows represent a real, currently-active WIC commitment.
        // IsOnSite=false rows are history (move-to-gsd), reassignment-to-other-task
        // markers (PATCH Voice/Backlog), or harmless Excel-import "Global Service Desk"
        // rows — none of them a live time conflict. See BLUEPRINT_LOGIC.md §7.2.
        var conflictCandidates = existingWicShifts.Where(w => w.IsOnSite).ToList();

        // Human decision (manualActor != null): an overlapping AUTOMATIC commitment at
        // another centre is REPLACED in the same SaveChanges — one manual action must win
        // over an automatic row and be visible everywhere at once.
        //
        // Requirement 5 (RTM / human assign, 2026-10-13): a centre A row is only protected
        // when BOTH hold —
        //   (a) WicAgentAssignments carries an ACTIVE row for A matching this employee
        //       (LocationCode + FullName; the table has no EmployeeId column), and
        //   (b) the row is human-owned, read from the day's ShiftEntries row exactly as
        //       before (ManualChangeBy set, or SourceModule == RolloutSource.Module),
        // because WicShiftEntries has no manual columns of its own.
        // Without an active permanent assignment for A there is nothing to protect, so the
        // human's new centre wins and the stale A row is removed — a human action must not
        // be blocked by a leftover WicShiftEntries row. When both hold, the row is never
        // deleted silently: the caller gets requiresConfirmation with who set it and when,
        // and re-sends with ConfirmManualReplace=true to replace it.
        //
        // An automatic writer (manualActor == null) keeps the historical, conservative
        // behaviour exactly: block, save nothing, and return the exact reason. It never
        // replaces or deletes anything.
        var toReplace      = new List<WicShiftEntry>();
        var manualConflict = new List<object>();
        var replacedHuman  = false;

        // WicAgentAssignments has no EmployeeId column: the active-owner lookup is by
        // (LocationCode, EmployeeName == employee.FullName). Loaded once, and only when a
        // human call actually has an overlapping on-site candidate to judge.
        var activeAssignmentLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (manualActor != null && conflictCandidates.Count > 0 && !string.IsNullOrWhiteSpace(employee.FullName))
        {
            var ownedCodes = await db.WicAgentAssignments
                .Where(a => a.IsActive && a.EmployeeName == employee.FullName)
                .Select(a => a.LocationCode)
                .ToListAsync();
            foreach (var code in ownedCodes)
                if (!string.IsNullOrWhiteSpace(code)) activeAssignmentLocations.Add(code);
        }

        // Human ownership of the day's row, exactly the check the previous implementation
        // used (ShiftEntries.ManualChangeBy, or a rollout-owned row).
        var isHumanOwned = shift != null && (!string.IsNullOrWhiteSpace(shift.ManualChangeBy)
            || string.Equals(shift.SourceModule, RolloutSource.Module, StringComparison.Ordinal));

        foreach (var existing in conflictCandidates)
        {
            // Same location → update in-place (no conflict)
            if (string.Equals(existing.SupportLocation, location.DisplayName, StringComparison.OrdinalIgnoreCase))
                continue;

            var (exStart, exEnd) = ParseShift(existing.WorkingShift);
            // If either side has unknown times, allow the assignment
            if (newStart == null || newEnd == null || exStart == null || exEnd == null)
                continue;
            // Overlap: [newStart, newEnd) intersects [exStart, exEnd)
            if (!(newStart < exEnd && exStart < newEnd))
                continue;

            var conflictLocation = existing.SupportLocation ?? existing.LocationCode ?? "(unknown)";

            // Automatic writer: the historical skip, with the exact day reason.
            if (manualActor == null)
                return Results.Ok(new
                {
                    success    = true,
                    saved      = false,
                    skipped    = true,
                    reasonCode = "TIME_CONFLICT",
                    reason     = $"Time conflict: agent already assigned to {conflictLocation} ({existing.WorkingShift}) on {date:yyyy-MM-dd}",
                    date       = date.ToString("yyyy-MM-dd"),
                    // Structured so the UI can render the sentence in EN or DE without
                    // embedding this English text in the German view.
                    conflicts  = new[] { new { location = conflictLocation, workingShift = existing.WorkingShift } },
                });

            // Requirement 5 step 1: an ACTIVE permanent assignment for centre A exists for
            // this employee (name + the A row's own LocationCode).
            var hasActiveAssignmentForA = !string.IsNullOrWhiteSpace(existing.LocationCode)
                && activeAssignmentLocations.Contains(existing.LocationCode);

            // Requirement 5 step 3: protected only when the active permanent assignment AND
            // human ownership coincide. Anything else falls through to toReplace (step 4).
            if (hasActiveAssignmentForA && isHumanOwned)
            {
                if (!req.ConfirmManualReplace)
                {
                    manualConflict.Add(new
                    {
                        location     = conflictLocation,
                        workingShift = existing.WorkingShift,
                        actor        = shift!.ManualChangeBy,
                        atUtc        = shift.ManualChangeAt,
                        atBerlin     = BerlinTime.FromUtc(shift.ManualChangeAt)?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    });
                    continue;
                }
                replacedHuman = true;
            }

            toReplace.Add(existing);
        }

        if (manualConflict.Count > 0)
            return Results.Ok(new
            {
                success              = true,
                saved                = false,
                requiresConfirmation = true,
                reasonCode           = "MANUAL_CONFLICT",
                reason               = $"The existing assignment on {date:yyyy-MM-dd} belongs to an active permanent WIC assignment and was set by a human; it must be confirmed before it can be replaced.",
                date                 = date.ToString("yyyy-MM-dd"),
                conflicts            = manualConflict,
            });

        // Replace = remove the superseded automatic/human-confirmed commitment so the
        // target centre is the agent's commitment for that day. Same SaveChanges below.
        foreach (var superseded in toReplace)
            db.WicShiftEntries.Remove(superseded);

        // Upsert the WicShiftEntry for this specific location (others stay intact)
        var existingForLoc = existingWicShifts
            .FirstOrDefault(w => string.Equals(w.SupportLocation, location.DisplayName, StringComparison.OrdinalIgnoreCase));
        if (existingForLoc != null)
        {
            existingForLoc.WorkingShift = workingShift;
            existingForLoc.LocationCode = req.LocationCode;
            existingForLoc.IsOnSite     = true;
            existingForLoc.IsGSDDay     = false;
            existingForLoc.Task         = "WIC";
        }
        else
        {
            db.WicShiftEntries.Add(new WicShiftEntry
            {
                EmployeeId      = req.EmployeeId,
                ShiftDate       = date,
                DayOfWeek       = date.DayOfWeek.ToString(),
                SupportLocation = location.DisplayName,
                LocationCode    = req.LocationCode,
                IsOnSite        = true,
                IsGSDDay        = false,
                IsOffDay        = false,
                WorkingShift    = workingShift,
                Task            = "WIC",
            });
        }

        // THE MULTI-CENTRE RULE (RTM 2026-10-15): when the day ends up with 2+ on-site WIC
        // centres, the single daily ShiftEntries row must span all of them — earliest WIC
        // part start .. latest WIC part end. The WicShiftEntries rows stay one per centre.
        //
        // The row is only ever WIDENED. A window that already covers the span (a wider
        // roster shift, an earlier multi-centre span) is left exactly as it is, so a later
        // edit of ONE centre can never push another centre out of the day.
        //
        // `keepAgentShift` is deliberately NOT excluded any more: the Demmin case is
        // precisely an agent whose own 13:00-15:00 window had to grow to 10:00-15:00 once
        // Am Hanseufer (10:00-12:00) was added next to Woldeforster (13:00-15:00).
        var dayShiftRow = shift ?? newShift;
        if (dayShiftRow != null)
        {
            var partWindows = existingWicShifts
                .Where(w => w.IsOnSite)          // only live commitments are WIC parts
                .Where(w => !toReplace.Contains(w))
                .Where(w => !string.Equals(w.SupportLocation, location.DisplayName, StringComparison.OrdinalIgnoreCase))
                .Select(w => w.WorkingShift)
                .Append(workingShift);
            if (WicWorkingShift.WidenDaily(dayShiftRow.ShiftStart, dayShiftRow.ShiftEnd, partWindows) is { } widened)
            {
                dayShiftRow.ShiftStart = widened.Start;
                dayShiftRow.ShiftEnd   = widened.End;
            }
        }

        if (manualActor != null && (shift ?? newShift) is { } manualTarget)
            db.RecordManualShiftEdit(manualTarget, manualActor);

        await db.SaveChangesAsync();
        return Results.Ok(new
        {
            success           = true,
            saved             = true,
            reasonCode        = replacedHuman
                ? "REPLACED_CONFIRMED"
                : toReplace.Count > 0 ? "REPLACED_AUTOMATIC" : "SAVED",
            reason            = toReplace.Count > 0
                ? $"Replaced the previous assignment at {string.Join(", ", toReplace.Select(r => $"{r.SupportLocation} ({r.WorkingShift})"))}."
                : null,
            replacedAutomatic = toReplace.Count > 0 && !replacedHuman,
            replaced          = toReplace.Select(r => new { location = r.SupportLocation, workingShift = r.WorkingShift }).ToList(),
            employeeName      = employee.FullName ?? req.EmployeeId,
            displayName       = location.DisplayName,
            date              = date.ToString("yyyy-MM-dd"),
            nppWarning,
        });
    }

    /// <summary>
    /// POST /api/wic/move-to-gsd — the team lead's deliberate "move to GSD Backlog"
    /// (WIC Shifts kebab menu, WIC Attendance agent chip, WIC Schedule, Assign-Agent
    /// dialog). Kept out of the endpoint lambda so the manual-marker rule is directly
    /// testable in-memory.
    ///
    /// <paramref name="manualActor"/> follows the same contract as
    /// <see cref="CreateShiftAsync"/> / <see cref="CreateAssignmentAsync"/>: non-null
    /// means THIS call is the human's decision, so it may replace a row another human
    /// set earlier and re-stamps it via <see cref="GSDContext.RecordManualShiftEdit"/>.
    /// A null actor (an automatic writer) still gets the protective 409 and moves nothing.
    /// </summary>
    public async Task<IResult> MoveToGsdAsync(
        string employeeId, DateOnly dateFrom, DateOnly dateTo, bool skipWeekends,
        GSDContext db, string? manualActor = null)
    {
        if (dateTo < dateFrom)
            return Results.BadRequest(new { error = "DateTo must not be before Date." });

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
        if (employee == null)
            return Results.NotFound(new { error = $"Employee '{employeeId}' not found." });

        var allDates = new List<DateOnly>();
        for (var d = dateFrom; d <= dateTo; d = d.AddDays(1))
        {
            if (skipWeekends && (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday))
                continue;
            allDates.Add(d);
        }
        if (allDates.Count == 0)
            return Results.BadRequest(new { error = "No dates to move (range is empty after skipping weekends)." });

        var moveCandidates = await db.ShiftEntries
            .Where(s => s.EmployeeId == employeeId && allDates.Contains(s.ShiftDate))
            .ToListAsync();
        // Only an automatic writer (no actor) is refused here. With an actor this is the
        // human's own move, exactly like CreateShiftAsync/CreateAssignmentAsync: the row is
        // re-stamped below so the guard lets THIS write through while every later automatic
        // writer still cannot touch it.
        if (manualActor == null)
        {
            var protectedRow = moveCandidates.FirstOrDefault(s => db.PreserveManualShift(s, "Move to GSD"));
            if (protectedRow != null)
                return Results.Conflict(new { error = $"Range contains a protected manual shift ({protectedRow.ShiftDate:yyyy-MM-dd}); edit the cell directly. Nothing moved." });
        }

        IResult? errorResult = null;
        var movedDates = new List<string>();

        async Task MoveAllDatesAsync()
        {
            movedDates.Clear();
            foreach (var date in allDates)
            {
                var shift = await db.ShiftEntries
                    .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.ShiftDate == date);
                if (shift != null)
                {
                    shift.ShiftType    = ShiftTypes.Working;
                    shift.AgentTask    = "GSD";
                    shift.IsWicDuty    = false;
                    shift.SourceModule = "MOVE_TO_GSD";
                }
                else
                {
                    shift = new ShiftEntry
                    {
                        EmployeeId   = employeeId,
                        ShiftDate    = date,
                        ShiftType    = ShiftTypes.Working,
                        AgentTask    = "GSD",
                        IsWicDuty    = false,
                        SourceSheet  = "MOVE_TO_GSD",
                        SourceModule = "MOVE_TO_GSD",
                    };
                    db.ShiftEntries.Add(shift);
                }

                // This row is now the human's decision, so the database guard must let THIS
                // same SaveChanges write it and every later automatic writer must leave it alone.
                if (manualActor != null)
                    db.RecordManualShiftEdit(shift, manualActor);

                var wicEntries = await db.WicShiftEntries
                    .Where(w => w.EmployeeId == employeeId && w.ShiftDate == date && w.IsOnSite)
                    .ToListAsync();
                foreach (var w in wicEntries)
                {
                    w.IsOnSite = false;
                    w.IsGSDDay = true;
                }

                movedDates.Add(date.ToString("yyyy-MM-dd"));
            }

            await db.SaveChangesAsync();
        }

        // The in-memory test provider has no transactions; real SQL Server keeps the
        // all-or-nothing guarantee this route always had.
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            try { await MoveAllDatesAsync(); }
            catch (Exception ex)
            {
                return Results.Conflict(new { error = "Move to GSD failed: " + ex.Message, detail = ex.Message });
            }
        }
        else
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                try
                {
                    await MoveAllDatesAsync();
                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    // Name the real reason instead of a bare "Operation failed": the frontend
                    // shows this text verbatim, and a swallowed cause is exactly what made the
                    // original incident look like a mystery.
                    errorResult = Results.Conflict(new { error = "Move to GSD failed: " + ex.Message, detail = ex.Message });
                }
            });
        }
        if (errorResult != null) return errorResult;

        return Results.Ok(new
        {
            success      = true,
            employeeName = employee.FullName ?? employeeId,
            date         = dateFrom.ToString("yyyy-MM-dd"),
            dateTo       = dateTo.ToString("yyyy-MM-dd"),
            movedDates,
            daysMoved    = movedDates.Count,
        });
    }
}

public static class WicEndpointMapper
{
    public static void MapWicEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/wic").WithTags("WIC");

        grp.MapGet("/", async (string? from, string? to, string? location, string? employeeId, string? teamLead, WicShiftService svc) =>
            Results.Ok(await svc.GetWicShiftsAsync(from, to, location, employeeId, teamLead)));

        grp.MapPatch("/{id:int}", async (int id, PatchWicShiftRequest req, WicShiftService svc) =>
        {
            var result = await svc.PatchWicShiftAsync(id, req);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        grp.MapGet("/locations", async (WicShiftService svc) =>
            Results.Ok(await svc.GetLocationsAsync()));

        grp.MapGet("/coverage", async (string? date, WicShiftService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetCoverageAsync(d));
        });

        grp.MapGet("/open", async (string? date, int? horizon, WicShiftService svc) =>
            Results.Ok(await svc.GetOpenAsync(date, horizon ?? 3)));

        grp.MapGet("/availableHours", async (string? from, string? to, string? teamLead, WicShiftService svc) =>
            Results.Ok(await svc.GetAvailableHoursAsync(from, to, teamLead)));

        grp.MapGet("/availableHours/download", async (string? from, string? to, string? teamLead, WicShiftService svc, HttpContext ctx) =>
        {
            var bytes = await svc.ExportAvailableHoursCsvAsync(from, to, teamLead);
            var filename = "AvailableHours_" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + filename + "\"";
            return Results.File(bytes, "text/csv");
        });

        grp.MapGet("/agent/{employeeId}", async (string employeeId, string? from, string? to, WicShiftService svc) =>
            Results.Ok(await svc.GetWicShiftsAsync(from, to, null, employeeId, null)));

        grp.MapGet("/download", async (string? from, string? to, WicShiftService svc, HttpContext ctx) =>
        {
            var bytes = await svc.ExportToExcelAsync(from, to);
            var filename = "WicReport_" + DateTime.Today.ToString("yyyy-MM-dd") + ".xlsx";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + filename + "\"";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        });

        // Both routes below are the interactive "+ New Shift" / "Assign to gap" UI
        // actions — every call here is a deliberate human decision, so it always
        // carries a manualActor and must always succeed (never hit PreserveManualShift).
        // Prefer the logged-in user's KID; while Auth:EnforceAuthorization is still in
        // its rollout phase (appsettings "Auth:EnforceAuthorization": false) most calls
        // arrive unauthenticated, so fall back to a fixed, non-null tag rather than
        // depending on login state — avoids any risk of silently reintroducing a block.
        static string ResolveManualActor(HttpContext ctx) =>
            ctx.User.FindFirst(AuthClaims.Kid)?.Value is { Length: > 0 and <= 100 } kid ? kid : "UI-MANUAL";

        grp.MapPost("/shifts", async (CreateShiftRequest req, GSDContext db, ShiftValidationService validationSvc, WicShiftService svc, HttpContext ctx) =>
            await svc.CreateShiftAsync(req, db, validationSvc, ResolveManualActor(ctx)));

        grp.MapPost("/assignments", async (CreateAssignmentRequest req, GSDContext db, WicShiftService svc, HttpContext ctx) =>
        {
            // "Assign anyway" is a role-gated override, enforced HERE — not by RbacMiddleware —
            // so it holds even while Auth:EnforceAuthorization is off (rollout phase).
            // DEV/RTM/TEAM_LEAD only; AGENT 403; anonymous 401.
            if (req.AssignAnyway)
            {
                var denied = CheckAssignAnywayAccess(ctx);
                if (denied != null) return denied;
            }
            return await svc.CreateAssignmentAsync(req, db, manualActor: ResolveManualActor(ctx));
        });

        // Move-to-GSD is an interactive team-lead action (the WIC Shifts kebab menu,
        // WIC Attendance agent chip, WIC Schedule and the Assign-Agent dialog). It obeys
        // the same actor rule as the "+ New Shift"/"Assign to gap" routes above: every
        // call carries a manualActor and may therefore change a row a human set earlier,
        // while an automatic caller (manualActor == null) still gets PreserveManualShift.
        // Before this, the route had no actor at all, so GSDContext.PreserveManualShift
        // rejected the team lead's own move with a bare 409/Warning (live: 2026-10-07
        // 07:06, ShiftEntries id 56294) and the UI showed only "Failed to move agent".
        // 2026-10-07: a strict session/KID gate (401/403) was considered but could not be
        // justified — no live request log for the last 7 days proves that every real
        // manual call arrives with a session, and an active team lead without a usable
        // KID would have been locked out. Variant b therefore reuses ResolveManualActor,
        // exactly like the two manual routes above and /api/shifts/*, so no team lead ever
        // loses the ability to make a manual change.
        grp.MapPost("/move-to-gsd", async (MoveToGsdRequest req, GSDContext db, WicShiftService svc, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(req.EmployeeId))
                return Results.BadRequest(new { error = "EmployeeId is required." });
            if (!DateOnly.TryParse(req.Date, out var dateFrom))
                return Results.BadRequest(new { error = "Invalid date format. Expected yyyy-MM-dd." });
            var dateToStr = string.IsNullOrWhiteSpace(req.DateTo) ? req.Date : req.DateTo;
            if (!DateOnly.TryParse(dateToStr, out var dateTo))
                return Results.BadRequest(new { error = "Invalid dateTo format. Expected yyyy-MM-dd." });

            // Variant b: the same ResolveManualActor contract as /shifts and /assignments.
            // A signed-in caller is stamped with its KID; an unauthenticated one (while
            // Auth:EnforceAuthorization is still off) gets the fixed UI-MANUAL tag, so the
            // move still succeeds and the row is still protected from later automatic
            // writers. The service-level null actor (an automatic caller) is never used here.
            return await svc.MoveToGsdAsync(
                req.EmployeeId, dateFrom, dateTo, req.SkipWeekends, db, ResolveManualActor(ctx));
        });
    }

    /// <summary>
    /// Role gate for the "Assign anyway" override. Enforced at the endpoint — not by
    /// RbacMiddleware — so it holds even while Auth:EnforceAuthorization is false.
    /// Mirrors ShiftEndpointMapper.CheckManualAccess: DEV/RTM/TEAM_LEAD pass (null),
    /// anonymous → 401, AGENT/other → 403.
    /// </summary>
    internal static IResult? CheckAssignAnywayAccess(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
            return Results.Json(new { error = "Authentication required" }, statusCode: StatusCodes.Status401Unauthorized);
        var role = ctx.User.FindFirst(AuthClaims.Role)?.Value;
        var actor = ctx.User.FindFirst(AuthClaims.Kid)?.Value;
        if (role is AppRoles.Dev or AppRoles.Rtm or AppRoles.TeamLead &&
            !string.IsNullOrWhiteSpace(actor) && actor.Length <= 100)
            return null;
        return Results.Json(new { error = "Forbidden" }, statusCode: StatusCodes.Status403Forbidden);
    }
}
