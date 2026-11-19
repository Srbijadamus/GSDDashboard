using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

// ── DTOs ───────────────────────────────────────────────────────────────────────

public record RtmPlanAgent(string EmployeeId, string Name, string? Start, string? End);

/// <summary>One location × one day of the FORECAST plan. Status is CLOSED, UNCOVERED or
/// COVERED. UNCOVERED means the centre is open but nobody is planned there; COVERED carries
/// the planned agents (a PARTIAL day is still COVERED here — the RTM sees the agents).</summary>
public record RtmPlanDay(string Date, string Status, List<RtmPlanAgent> Agents);

public record RtmLocationPlan(string LocationCode, string City, string DisplayName, string Country, List<RtmPlanDay> Days);

public record RtmPlanResponse(string StartDate, string EndDate, List<RtmLocationPlan> Locations);

public record RtmPresentAgent(string EmployeeId, string Name, string? CheckIn, string? CheckOut);

/// <summary>One location × one day of ACTUAL attendance. Status is CLOSED, NOT_PRESENT or
/// PRESENT. PRESENT lists who was actually on site (kiosk check-in at a WIC, home office
/// excluded); PlannedAbsent lists the planned agents who did NOT show up on site.</summary>
public record RtmAttendanceDay(string Date, string Status, List<RtmPresentAgent> Present, List<string> PlannedAbsent);

public record RtmLocationAttendance(string LocationCode, string City, string DisplayName, string Country,
    int OpenDays, int DaysWithPresence, List<RtmAttendanceDay> Days);

public record RtmAttendanceResponse(string StartDate, string EndDate, List<RtmLocationAttendance> Locations);

public record RtmCorrectionRow(string Date, string LocationCode, string Location, string Before, string Now);

public record RtmSnapshotInfo(string ReportType, string? PeriodFrom, string? PeriodTo, string? CreatedAt, string? CreatedBy);

public record RtmWeeklyResponse(RtmPlanResponse Plan, RtmSnapshotInfo? LastWeeklySent);

public record RtmCorrectionsResponse(bool NoWeeklySent, RtmSnapshotInfo? LastSent, List<RtmCorrectionRow> Changes);

public record MarkSentRequest(string ReportType, string? PeriodFrom, string? PeriodTo);

// ── Service ────────────────────────────────────────────────────────────────────

/// <summary>
/// RTM Reports: 7-day WIC forecast, daily corrections since the last sent report, and the
/// actual on-site attendance of last week. Read-only against GSDDashboard + ShiftKioskDB
/// (the latter through the same KioskProxyService WIC Attendance already uses).
///
/// The FORECAST plan reuses <see cref="WicPresenceResolver"/> exactly like the 28-day
/// outlook (<see cref="ForecastService"/>) — it only re-FORMATS the resolved presence into
/// names + times; there is no new coverage logic and no drift from the existing surfaces.
/// </summary>
public class RtmReportService
{
    private readonly GSDContext _db;
    private readonly IKioskProxy _proxy;

    public RtmReportService(GSDContext db, IKioskProxy proxy)
    {
        _db = db;
        _proxy = proxy;
    }

    private static readonly HashSet<string> PresentStatuses = new(StringComparer.OrdinalIgnoreCase)
    { "ACTIVE", "DONE", "PAUSE" };

    // ── Plan (forecast) — same source as the 28-day outlook ────────────────────

    public async Task<RtmPlanResponse> GetPlanAsync(DateOnly from, DateOnly to)
        => await LoadPlanAsync(from, to);

    /// <summary>Loads and resolves the WIC plan for [from, to] through the same primitives
    /// ForecastService uses, then projects the contributing agents into name + time.</summary>
    private async Task<RtmPlanResponse> LoadPlanAsync(DateOnly from, DateOnly to)
    {
        var locations = await _db.WicLocations
            .Where(l => l.IsActive)
            .OrderBy(l => l.Country).ThenBy(l => l.City)
            .ToListAsync();

        var allHours       = await _db.WicOpeningHours.ToListAsync();
        var publicHolidays = await _db.PublicHolidays
            .Where(ph => ph.HolidayDate >= from && ph.HolidayDate <= to)
            .ToListAsync();
        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= from && w.ShiftDate <= to)
            .Join(_db.Employees.Where(e => e.IsActive && !TestAccounts.ExcludedEmployeeIds.Contains(e.EmployeeId)),
                  w => w.EmployeeId, e => e.EmployeeId, (w, e) => w)
            .ToListAsync();
        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to)
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));
        var sickLeaves = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && sl.FirstDay <= to && sl.LastDay >= from)
            .ToListAsync();

        var wicDutyEntriesAll = WicPresenceResolver.CandidateDutyRows(shiftEntries)
            .Where(s => !TestAccounts.IsExcluded(s.EmployeeId)).ToList();

        var activeEmployees = await _db.Employees
            .Where(e => e.IsActive && !TestAccounts.ExcludedEmployeeIds.Contains(e.EmployeeId))
            .ToListAsync();
        var empById = activeEmployees
            .ToDictionary(e => e.EmployeeId, e => e, StringComparer.OrdinalIgnoreCase);
        var activeAssignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();
        var boIndex = WicBackOfficePriority.BuildIndex(activeAssignments, locations, activeEmployees);

        var locPlans = new List<RtmLocationPlan>();
        foreach (var loc in locations)
        {
            var days = new List<RtmPlanDay>();
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                int dow = (int)date.DayOfWeek;
                var hours = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy, dow, date);
                bool isHoliday = HolidayMatcher.IsHoliday(loc, date, publicHolidays);
                bool isClosed = hours == null || hours.IsClosed || isHoliday;

                if (isClosed)
                {
                    days.Add(new RtmPlanDay(date.ToString("yyyy-MM-dd"), "CLOSED", new List<RtmPlanAgent>()));
                    continue;
                }

                var presence = WicPresenceResolver.Resolve(
                    loc, date, wicEntries, shiftByEmpDate, wicDutyEntriesAll,
                    empId => sickLeaves.Any(sl => sl.EmployeeId == empId && sl.FirstDay <= date && sl.LastDay >= date),
                    boIndex, allHours);

                var contributors = presence.Agents.Where(a => a.Contribution > 0).ToList();
                if (contributors.Count == 0)
                {
                    days.Add(new RtmPlanDay(date.ToString("yyyy-MM-dd"), "UNCOVERED", new List<RtmPlanAgent>()));
                    continue;
                }

                var agents = contributors
                    .Select(a =>
                    {
                        var name = empById.TryGetValue(a.EmployeeId, out var emp)
                            ? (emp.FullName ?? $"{emp.FirstName} {emp.LastName}".Trim())
                            : a.EmployeeId;
                        var (start, end) = WorkingWindow(a.Shift, a.OnSiteRow);
                        return new RtmPlanAgent(a.EmployeeId, name, start, end);
                    })
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                days.Add(new RtmPlanDay(date.ToString("yyyy-MM-dd"), "COVERED", agents));
            }
            locPlans.Add(new RtmLocationPlan(
                loc.LocationCode, loc.City ?? "", loc.DisplayName, loc.Country ?? "DE", days));
        }

        return new RtmPlanResponse(from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), locPlans);
    }

    // ── Last week attendance (actual presence) ─────────────────────────────────

    public async Task<RtmAttendanceResponse> GetLastWeekAsync(DateOnly monday)
    {
        var friday = monday.AddDays(4);
        var plan = await LoadPlanAsync(monday, friday);

        var kioskByDate = new Dictionary<DateOnly, Dictionary<string, KioskAgentRecord>>();
        for (var d = monday; d <= friday; d = d.AddDays(1))
            kioskByDate[d] = await GetKioskForDateAsync(d);

        var locList = new List<RtmLocationAttendance>();
        foreach (var loc in plan.Locations)
        {
            var days = new List<RtmAttendanceDay>();
            var openDays = 0;
            var presenceDays = 0;

            foreach (var day in loc.Days)
            {
                var date = DateOnly.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (day.Status == "CLOSED")
                {
                    days.Add(new RtmAttendanceDay(day.Date, "CLOSED",
                        new List<RtmPresentAgent>(), new List<string>()));
                    continue;
                }

                openDays++;
                var kiosk = kioskByDate.TryGetValue(date, out var map) ? map : new Dictionary<string, KioskAgentRecord>();
                var present = new List<RtmPresentAgent>();
                var absent = new List<string>();

                foreach (var agent in day.Agents)
                {
                    if (kiosk.TryGetValue(agent.EmployeeId, out var rec) &&
                        PresentStatuses.Contains(rec.AttendanceStatus ?? "") &&
                        !string.Equals(rec.WorkLocation?.Trim(), "HOME_OFFICE", StringComparison.OrdinalIgnoreCase))
                    {
                        present.Add(new RtmPresentAgent(
                            agent.EmployeeId, agent.Name,
                            ToHhMm(rec.CheckInTime), ToHhMm(rec.CheckOutTime)));
                    }
                    else
                    {
                        absent.Add(agent.Name);
                    }
                }

                var status = present.Count == 0 ? "NOT_PRESENT" : "PRESENT";
                if (present.Count > 0) presenceDays++;
                days.Add(new RtmAttendanceDay(day.Date, status, present, absent));
            }

            locList.Add(new RtmLocationAttendance(
                loc.LocationCode, loc.City, loc.DisplayName, loc.Country,
                openDays, presenceDays, days));
        }

        return new RtmAttendanceResponse(monday.ToString("yyyy-MM-dd"), friday.ToString("yyyy-MM-dd"), locList);
    }

    // ── Daily corrections ──────────────────────────────────────────────────────

    public async Task<RtmCorrectionsResponse> GetCorrectionsAsync()
    {
        var hasWeekly = await _db.RtmReportSnapshots.AnyAsync(s => s.ReportType == "WEEKLY");
        if (!hasWeekly)
            return new RtmCorrectionsResponse(true, null, new List<RtmCorrectionRow>());

        var last = await _db.RtmReportSnapshots
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .FirstAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var changes = new List<RtmCorrectionRow>();

        if (last.PeriodTo >= today && !string.IsNullOrWhiteSpace(last.PayloadJson))
        {
            RtmPlanResponse? before = null;
            try { before = JsonSerializer.Deserialize<RtmPlanResponse>(last.PayloadJson); }
            catch { before = null; }

            if (before != null)
            {
                var beforeMap = before.Locations
                    .SelectMany(l => l.Days.Select(d => (l.LocationCode, l.City, d)))
                    .ToDictionary(x => (x.LocationCode, x.d.Date), x => (x.City, Text: PlanCellText(x.d)));

                var nowPlan = await LoadPlanAsync(today, last.PeriodTo);
                foreach (var loc in nowPlan.Locations)
                {
                    foreach (var day in loc.Days)
                    {
                        if (!beforeMap.TryGetValue((loc.LocationCode, day.Date), out var b)) continue;
                        var nowText = PlanCellText(day);
                        if (string.Equals(b.Text, nowText, StringComparison.Ordinal)) continue;
                        changes.Add(new RtmCorrectionRow(day.Date, loc.LocationCode, loc.City, b.Text, nowText));
                    }
                }
                changes = changes.OrderBy(c => c.Date, StringComparer.Ordinal)
                                 .ThenBy(c => c.Location, StringComparer.OrdinalIgnoreCase)
                                 .ToList();
            }
        }

        return new RtmCorrectionsResponse(false, MapInfo(last), changes);
    }

    // ── Snapshots (Mark as sent) ───────────────────────────────────────────────

    public async Task<RtmSnapshotInfo?> GetLastWeeklySentAsync()
    {
        var snap = await _db.RtmReportSnapshots
            .Where(s => s.ReportType == "WEEKLY")
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync();
        return snap == null ? null : MapInfo(snap);
    }

    public async Task<(RtmSnapshotInfo Info, string? Error)> MarkSentAsync(
        string reportType, DateOnly from, DateOnly to, string? actor)
    {
        if (reportType is not ("WEEKLY" or "DAILY"))
            return (null!, "Report type must be WEEKLY or DAILY.");
        if (to < from)
            return (null!, "PeriodTo must not be before PeriodFrom.");

        // Snapshot the plan the report DISPLAYED, recomputed server-side for the same
        // period, so "daily corrections" always diffs against the same canonical shape.
        var plan = await LoadPlanAsync(from, to);
        var payload = JsonSerializer.Serialize(plan);

        var snap = new RtmReportSnapshot
        {
            ReportType  = reportType,
            PeriodFrom  = from,
            PeriodTo    = to,
            CreatedAt   = DateTime.UtcNow,
            CreatedBy   = actor,
            PayloadJson = payload,
        };
        _db.RtmReportSnapshots.Add(snap);
        await _db.SaveChangesAsync();
        return (MapInfo(snap), null);
    }

    // ── Canonical cell text (display + diff + Excel) ───────────────────────────

    public static string PlanCellText(RtmPlanDay day)
    {
        if (day.Status == "CLOSED") return "Closed";
        if (day.Status == "UNCOVERED") return "Uncovered";
        return string.Join("\n", day.Agents.Select(a =>
            a.Start != null && a.End != null ? $"{a.Name} {a.Start}–{a.End}" : a.Name));
    }

    public static string AttendanceCellText(RtmAttendanceDay day)
    {
        if (day.Status == "CLOSED") return "Closed";
        if (day.Status == "NOT_PRESENT") return "Not present";
        var lines = new List<string>();
        foreach (var p in day.Present)
        {
            var time = p.CheckIn != null ? (p.CheckOut != null ? $"{p.CheckIn}–{p.CheckOut}" : p.CheckIn) : null;
            lines.Add(time != null ? $"{p.Name} {time}" : p.Name);
        }
        foreach (var n in day.PlannedAbsent) lines.Add($"Planned: {n}");
        return string.Join("\n", lines);
    }

    // ── Excel exports ──────────────────────────────────────────────────────────

    public async Task<byte[]> ExportWeeklyToExcelAsync(DateOnly start)
    {
        var plan = await LoadPlanAsync(start, start.AddDays(6));
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Weekly forecast");
        ws.Cell(1, 1).Value = "Location";
        for (var i = 0; i < 7; i++)
            ws.Cell(1, i + 2).Value = DayHeader(start.AddDays(i));
        StyleHeader(ws, 1, 8);
        var r = 2;
        foreach (var loc in plan.Locations)
        {
            ws.Cell(r, 1).Value = $"{loc.City} ({loc.LocationCode})";
            for (var i = 0; i < loc.Days.Count && i < 7; i++)
                ws.Cell(r, i + 2).Value = PlanCellText(loc.Days[i]);
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportLastWeekToExcelAsync(DateOnly monday)
    {
        var att = await GetLastWeekAsync(monday);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Last week attendance");
        ws.Cell(1, 1).Value = "Location";
        for (var i = 0; i < 5; i++)
            ws.Cell(1, i + 2).Value = DayHeader(monday.AddDays(i));
        ws.Cell(1, 7).Value = "Open days";
        ws.Cell(1, 8).Value = "Days with presence";
        StyleHeader(ws, 1, 8);
        var r = 2;
        foreach (var loc in att.Locations)
        {
            ws.Cell(r, 1).Value = $"{loc.City} ({loc.LocationCode})";
            for (var i = 0; i < loc.Days.Count && i < 5; i++)
                ws.Cell(r, i + 2).Value = AttendanceCellText(loc.Days[i]);
            ws.Cell(r, 7).Value = loc.OpenDays;
            ws.Cell(r, 8).Value = loc.DaysWithPresence;
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportCorrectionsToExcelAsync()
    {
        var corr = await GetCorrectionsAsync();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Daily corrections");
        var headers = new[] { "Date", "Location", "Before", "Now" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#b91c1c");
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        var r = 2;
        foreach (var c in corr.Changes)
        {
            ws.Cell(r, 1).Value = c.Date;
            ws.Cell(r, 2).Value = $"{c.Location} ({c.LocationCode})";
            ws.Cell(r, 3).Value = c.Before;
            ws.Cell(r, 4).Value = c.Now;
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static (string? Start, string? End) WorkingWindow(ShiftEntry? sh, WicShiftEntry? onSite)
    {
        var (wsStart, wsEnd) = SplitWorkingShift(onSite?.WorkingShift);
        return (wsStart ?? sh?.ShiftStart, wsEnd ?? sh?.ShiftEnd);
    }

    private static (string? Start, string? End) SplitWorkingShift(string? workingShift)
    {
        if (string.IsNullOrWhiteSpace(workingShift)) return (null, null);
        var times = System.Text.RegularExpressions.Regex.Matches(workingShift, @"\d{1,2}:\d{2}");
        return times.Count >= 2 ? (times[0].Value, times[^1].Value) : (null, null);
    }

    private static string DayHeader(DateOnly d) =>
        d.ToString("ddd dd.MM", CultureInfo.InvariantCulture);

    private static void StyleHeader(IXLWorksheet ws, int row, int cols)
    {
        for (var i = 1; i <= cols; i++)
        {
            ws.Cell(row, i).Style.Font.Bold = true;
            ws.Cell(row, i).Style.Fill.BackgroundColor = XLColor.FromHtml("#b91c1c");
            ws.Cell(row, i).Style.Font.FontColor = XLColor.White;
        }
    }

    private static string? ToHhMm(string? dt)
    {
        // Kiosk timestamps are "YYYY-MM-DD HH:MM:SS[.ffffff]" (str() of a pyodbc datetime);
        // "HH:MM" sits at [11,16). Handles the ISO 'T' separator too.
        if (string.IsNullOrWhiteSpace(dt) || dt.Length < 16) return null;
        var hhmm = dt.Substring(11, 5);
        return hhmm.All(c => char.IsDigit(c) || c == ':') ? hhmm : null;
    }

    private static string? GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private async Task<Dictionary<string, KioskAgentRecord>> GetKioskForDateAsync(DateOnly date)
    {
        var result = new Dictionary<string, KioskAgentRecord>(StringComparer.OrdinalIgnoreCase);
        var (status, body) = await _proxy.GetAttendanceAsync(date.ToString("yyyy-MM-dd"), null);
        if (status != 200) return result;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var empId = GetStr(el, "employee_id");
                if (string.IsNullOrWhiteSpace(empId)) continue;
                var rec = new KioskAgentRecord(
                    empId,
                    GetStr(el, "full_name") ?? empId,
                    GetStr(el, "attendance_status"),
                    GetStr(el, "checkin_time"),
                    GetStr(el, "checkout_time"),
                    GetStr(el, "work_location"));
                result[empId] = rec;
            }
        }
        catch { /* invalid feed → no kiosk data for this day */ }
        return result;
    }

    private static RtmSnapshotInfo MapInfo(RtmReportSnapshot s) =>
        new(s.ReportType,
            s.PeriodFrom.ToString("yyyy-MM-dd"),
            s.PeriodTo.ToString("yyyy-MM-dd"),
            s.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            s.CreatedBy);

    private sealed record KioskAgentRecord(
        string EmployeeId, string Name, string? AttendanceStatus,
        string? CheckInTime, string? CheckOutTime, string? WorkLocation);
}

// ── Endpoints (DEV and RTM only; 401 without a session, 403 otherwise) ────────

public static class RtmReportEndpointMapper
{
    public static void MapRtmReportEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/rtm-reports").WithTags("RTM Reports");

        static IResult? Deny(HttpContext ctx)
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Json(new { error = "Authentication required" },
                                    statusCode: StatusCodes.Status401Unauthorized);
            var role = ctx.User.FindFirst(AuthClaims.Role)?.Value;
            if (role is not (AppRoles.Dev or AppRoles.Rtm))
                return Results.Json(new { error = "Forbidden" },
                                    statusCode: StatusCodes.Status403Forbidden);
            return null;
        }

        static string ResolveActor(HttpContext ctx) =>
            ctx.User.FindFirst(AuthClaims.Kid)?.Value is { Length: > 0 and <= 100 } kid ? kid : "UI-MANUAL";

        grp.MapGet("/weekly", async (string? start, RtmReportService svc, HttpContext ctx) =>
        {
            if (Deny(ctx) is { } deny) return deny;
            var from = start != null && DateOnly.TryParse(start, out var d)
                ? d : DateOnly.FromDateTime(DateTime.Today).AddDays(1);
            var plan = await svc.GetPlanAsync(from, from.AddDays(6));
            var last = await svc.GetLastWeeklySentAsync();
            return Results.Ok(new RtmWeeklyResponse(plan, last));
        });

        grp.MapGet("/last-week", async (string? monday, RtmReportService svc, HttpContext ctx) =>
        {
            if (Deny(ctx) is { } deny) return deny;
            DateOnly from;
            if (monday != null && DateOnly.TryParse(monday, out var d))
                from = d;
            else
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var diff = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                from = today.AddDays(-diff).AddDays(-7); // last week's Monday
            }
            return Results.Ok(await svc.GetLastWeekAsync(from));
        });

        grp.MapGet("/corrections", async (RtmReportService svc, HttpContext ctx) =>
        {
            if (Deny(ctx) is { } deny) return deny;
            return Results.Ok(await svc.GetCorrectionsAsync());
        });

        grp.MapPost("/mark-sent", async (MarkSentRequest req, RtmReportService svc, HttpContext ctx) =>
        {
            if (Deny(ctx) is { } deny) return deny;
            if (!DateOnly.TryParse(req.PeriodFrom, out var from) ||
                !DateOnly.TryParse(req.PeriodTo, out var to))
                return Results.BadRequest(new { error = "PeriodFrom and PeriodTo must be valid yyyy-MM-dd dates." });
            var (info, error) = await svc.MarkSentAsync(req.ReportType, from, to, ResolveActor(ctx));
            if (error != null) return Results.BadRequest(new { error });
            return Results.Ok(info);
        });

        static string ExcelHeaders(HttpContext ctx, string filename)
        {
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        }

        grp.MapGet("/download/weekly", async (string? start, RtmReportService svc, HttpContext ctx) =>
        {
            if (Deny(ctx) is { } deny) return deny;
            var from = start != null && DateOnly.TryParse(start, out var d)
                ? d : DateOnly.FromDateTime(DateTime.Today).AddDays(1);
            var bytes = await svc.ExportWeeklyToExcelAsync(from);
            return Results.File(bytes, ExcelHeaders(ctx, $"RTM_Weekly_{from:yyyy-MM-dd}.xlsx"));
        });

        grp.MapGet("/download/last-week", async (string? monday, RtmReportService svc, HttpContext ctx) =>
        {
            if (Deny(ctx) is { } deny) return deny;
            DateOnly from;
            if (monday != null && DateOnly.TryParse(monday, out var d)) from = d;
            else
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var diff = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                from = today.AddDays(-diff).AddDays(-7);
            }
            var bytes = await svc.ExportLastWeekToExcelAsync(from);
            return Results.File(bytes, ExcelHeaders(ctx, $"RTM_LastWeek_{from:yyyy-MM-dd}.xlsx"));
        });

        grp.MapGet("/download/corrections", async (RtmReportService svc, HttpContext ctx) =>
        {
            if (Deny(ctx) is { } deny) return deny;
            var bytes = await svc.ExportCorrectionsToExcelAsync();
            return Results.File(bytes, ExcelHeaders(ctx, $"RTM_Corrections_{DateTime.Today:yyyy-MM-dd}.xlsx"));
        });
    }
}
