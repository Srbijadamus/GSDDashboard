using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
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

public record CreateAssignmentRequest(
    string EmployeeId,
    string LocationCode,
    string Date,
    string? ShiftStart,
    string? ShiftEnd
);

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
                var hours = allHours.FirstOrDefault(h =>
                    (h.LocationCode == loc.LocationCode ||
                     (loc.LocationCodeLegacy != null && h.LocationCode == loc.LocationCodeLegacy)) &&
                    h.DayOfWeek == dow);

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

                // HALF_AL = 0.5 coverage (consistent with SubstitutionService)
                int fullAbsentCount = 0;
                double presentDouble = 0;
                foreach (var w in dayWic)
                {
                    if (absentToday.Contains(w.EmployeeId))
                        fullAbsentCount++;
                    else
                    {
                        shiftByEmpDate.TryGetValue((w.EmployeeId, date), out var sh);
                        if (sh != null && string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase))
                            presentDouble += 0.5;
                        else
                            presentDouble += 1.0;
                    }
                }

                int scheduledCount = dayWic.Count;
                int effectiveCoverage = (int)Math.Floor(presentDouble);
                int absentCount = fullAbsentCount;
                int minReq = loc.MinAgentsRequired ?? 1;

                string coverageStatus = CoverageEvaluator.Classify(isClosed, effectiveCoverage, minReq, closedReason).Status.ToString();

                return new WicDayStatusDto(
                    loc.LocationCode, loc.DisplayName, loc.City ?? "", loc.Country ?? "DE",
                    !isClosed, closedReason, intervals,
                    coverageStatus, scheduledCount, absentCount, effectiveCoverage,
                    loc.MinAgentsRequired);
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

            var oh = openingHours.FirstOrDefault(o =>
                (o.LocationCode == loc.LocationCode ||
                 (loc.LocationCodeLegacy != null && o.LocationCode == loc.LocationCodeLegacy)) &&
                o.DayOfWeek == dow);
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

        grp.MapPost("/shifts", async (CreateShiftRequest req, GSDContext db) =>
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
            if (shift != null)
            {
                shift.ShiftType    = req.ShiftType;
                shift.ShiftStart   = req.ShiftStart;
                shift.ShiftEnd     = req.ShiftEnd;
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

            if (!string.IsNullOrWhiteSpace(req.LocationCode) && req.ShiftType == ShiftTypes.WicDuty)
            {
                var location = await db.WicLocations
                    .FirstOrDefaultAsync(l => l.LocationCode == req.LocationCode && l.IsActive);
                if (location != null)
                {
                    int dow = (int)date.DayOfWeek;
                    var hours = await db.WicOpeningHours
                        .FirstOrDefaultAsync(h => h.LocationCode == req.LocationCode && h.DayOfWeek == dow);
                    string openTime  = hours?.OpenTime  ?? req.ShiftStart ?? "08:00";
                    string closeTime = hours?.CloseTime ?? req.ShiftEnd   ?? "17:00";
                    var wicShift = await db.WicShiftEntries
                        .FirstOrDefaultAsync(w => w.EmployeeId == req.EmployeeId && w.ShiftDate == date);
                    if (wicShift != null)
                    {
                        wicShift.SupportLocation = location.DisplayName;
                        wicShift.IsOnSite        = true;
                        wicShift.IsOffDay        = false;
                        wicShift.IsGSDDay        = false;
                        wicShift.WorkingShift    = $"{openTime}-{closeTime}";
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
                            IsOnSite        = true,
                            IsGSDDay        = false,
                            IsOffDay        = false,
                            WorkingShift    = $"{openTime}-{closeTime}",
                            Task            = "WIC",
                        });
                    }
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                success      = true,
                employeeName = employee.FullName ?? req.EmployeeId,
                shiftDate    = date.ToString("yyyy-MM-dd"),
                shiftType    = req.ShiftType,
            });
        });

        grp.MapPost("/assignments", async (CreateAssignmentRequest req, GSDContext db) =>
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
            var hours    = WicHoursResolver.Resolve(allHours, req.LocationCode, dow, date);
            if (hours?.IsClosed == true)
                return Results.Ok(new
                {
                    success = true,
                    skipped = true,
                    reason  = $"WIC location is closed on {date:yyyy-MM-dd}",
                    date    = date.ToString("yyyy-MM-dd"),
                });
            string openTime  = hours?.OpenTime  ?? req.ShiftStart ?? "08:00";
            string closeTime = hours?.CloseTime ?? req.ShiftEnd   ?? "17:00";

            var agentTask = (location.DisplayName?.Length ?? 0) > 20
                ? location.DisplayName![..20]
                : location.DisplayName ?? req.LocationCode[..Math.Min(20, req.LocationCode.Length)];

            var shift = await db.ShiftEntries
                .FirstOrDefaultAsync(s => s.EmployeeId == req.EmployeeId && s.ShiftDate == date);

            // Skip dates where the agent is on a non-working shift; never overwrite AL/SL/OFF/PH etc.
            if (shift != null)
            {
                var blockingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "AL", "HALF_AL", "SL", "UL", "PH", "LPH", "OFF", "OFF_WEEKEND", "OL", "RESIGNED" };
                if (blockingTypes.Contains(shift.ShiftType ?? ""))
                    return Results.Ok(new
                    {
                        success = true,
                        skipped = true,
                        reason  = $"Agent has shift type '{shift.ShiftType}' on {date:yyyy-MM-dd}",
                        date    = date.ToString("yyyy-MM-dd"),
                    });
            }

            if (shift != null)
            {
                shift.ShiftType    = ShiftTypes.WicDuty;
                shift.IsWicDuty    = true;
                shift.AgentTask    = agentTask;
                shift.ShiftStart   = req.ShiftStart ?? openTime;
                shift.ShiftEnd     = req.ShiftEnd   ?? closeTime;
                shift.SourceModule = "ASSIGN";
            }
            else
            {
                db.ShiftEntries.Add(new ShiftEntry
                {
                    EmployeeId   = req.EmployeeId,
                    ShiftDate    = date,
                    ShiftType    = ShiftTypes.WicDuty,
                    IsWicDuty    = true,
                    AgentTask    = agentTask,
                    ShiftStart   = req.ShiftStart ?? openTime,
                    ShiftEnd     = req.ShiftEnd   ?? closeTime,
                    SourceSheet  = "ASSIGN",
                    SourceModule = "ASSIGN",
                });
            }

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
            var (newStart, newEnd) = ParseShift($"{openTime}-{closeTime}");

            foreach (var existing in existingWicShifts)
            {
                // Same location → update in-place (no conflict)
                if (string.Equals(existing.SupportLocation, location.DisplayName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var (exStart, exEnd) = ParseShift(existing.WorkingShift);
                // If either side has unknown times, allow the assignment
                if (newStart == null || newEnd == null || exStart == null || exEnd == null)
                    continue;
                // Overlap: [newStart, newEnd) intersects [exStart, exEnd)
                if (newStart < exEnd && exStart < newEnd)
                    return Results.Ok(new
                    {
                        success = true,
                        skipped = true,
                        reason  = $"Time conflict: agent already assigned to {existing.SupportLocation} ({existing.WorkingShift}) on {date:yyyy-MM-dd}",
                        date    = date.ToString("yyyy-MM-dd"),
                    });
            }

            // Upsert the WicShiftEntry for this specific location (others stay intact)
            var existingForLoc = existingWicShifts
                .FirstOrDefault(w => string.Equals(w.SupportLocation, location.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (existingForLoc != null)
            {
                existingForLoc.WorkingShift = $"{openTime}-{closeTime}";
                existingForLoc.LocationCode = req.LocationCode;
                existingForLoc.IsOnSite     = true;
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
                    WorkingShift    = $"{openTime}-{closeTime}",
                    Task            = "WIC",
                });
            }

            // Update ShiftEntry time to span all WIC assignments for the day (earliest start → latest end)
            if (existingWicShifts.Count > 0)
            {
                var allShifts = existingWicShifts
                    .Where(w => !string.Equals(w.SupportLocation, location.DisplayName, StringComparison.OrdinalIgnoreCase))
                    .Select(w => ParseShift(w.WorkingShift))
                    .Append((newStart, newEnd))
                    .Where(t => t.Item1 != null && t.Item2 != null)
                    .ToList();
                if (allShifts.Count > 1 && shift != null)
                {
                    var minStart = allShifts.Select(t => t.Item1!.Value).Min();
                    var maxEnd   = allShifts.Select(t => t.Item2!.Value).Max();
                    shift.ShiftStart = minStart.ToString(@"hh\:mm");
                    shift.ShiftEnd   = maxEnd.ToString(@"hh\:mm");
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                success      = true,
                employeeName = employee.FullName ?? req.EmployeeId,
                displayName  = location.DisplayName,
                date         = date.ToString("yyyy-MM-dd"),
                nppWarning,
            });
        });
    }
}
