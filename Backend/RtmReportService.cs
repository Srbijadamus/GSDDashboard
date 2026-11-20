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

        // Active WicLocations keyed by the postal code embedded in LocationCode
        // ("DE~<PLZ>~<City>~<Street>"). The kiosk's wic_centers.location is the same
        // string, so the numeric PLZ is the stable join key. (WicLocations.PostalCode is
        // NOT populated in the live DB — hence the extraction from LocationCode.)
        var locations = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var byPostal = locations
            .Select(l => (Loc: l, Plz: PlzOf(l.LocationCode)))
            .Where(x => x.Plz != null)
            .GroupBy(x => x.Plz!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Loc).ToList());

        var kioskByDate = new Dictionary<DateOnly, Dictionary<string, KioskAgentRecord>>();
        for (var d = monday; d <= friday; d = d.AddDays(1))
            kioskByDate[d] = await GetKioskForDateAsync(d);

        // Planned agents per (location, date), plus a reverse lookup so an agent with no
        // day_wic_center_id falls back to the location(s) they were planned at.
        var plannedIds = new Dictionary<(string Loc, string Date), HashSet<string>>();
        var plannedNames = new Dictionary<(string Loc, string Date), Dictionary<string, string>>();
        var plannedLocsByAgent = new Dictionary<(string Date, string Emp), List<string>>();
        foreach (var loc in plan.Locations)
            foreach (var day in loc.Days)
            {
                if (day.Status == "CLOSED") continue;
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in day.Agents)
                {
                    ids.Add(a.EmployeeId);
                    names[a.EmployeeId] = a.Name;
                    var key = (day.Date, a.EmployeeId);
                    if (!plannedLocsByAgent.TryGetValue(key, out var list))
                        plannedLocsByAgent[key] = list = new List<string>();
                    list.Add(loc.LocationCode);
                }
                plannedIds[(loc.LocationCode, day.Date)] = ids;
                plannedNames[(loc.LocationCode, day.Date)] = names;
            }

        // Actual presence per (date, location). A HOME_OFFICE check-in is never presence;
        // otherwise the day is attributed to the centre the agent checked in at
        // (day_wic_center_id -> WicLocations) or, without a centre, to their planned centre.
        var present = new Dictionary<(string Date, string Loc), List<(string Emp, string Name, string? In, string? Out)>>();
        foreach (var (date, kiosk) in kioskByDate)
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            foreach (var r in kiosk.Values)
            {
                if (!PresentStatuses.Contains(r.AttendanceStatus ?? "")) continue;
                if (string.Equals(r.DayWorkLocation?.Trim(), "HOME_OFFICE", StringComparison.OrdinalIgnoreCase))
                    continue;
                List<string> targets;
                var mapped = MapKioskCenterToLocation(r.DayWicCenterLocation, byPostal);
                if (mapped != null) targets = new List<string> { mapped };
                else if (plannedLocsByAgent.TryGetValue((dateStr, r.EmployeeId), out var list)) targets = list;
                else targets = new List<string>();

                foreach (var loc in targets)
                {
                    var key = (dateStr, loc);
                    if (!present.TryGetValue(key, out var list2))
                        present[key] = list2 = new List<(string, string, string?, string?)>();
                    list2.Add((r.EmployeeId, r.Name, ToHhMm(r.CheckInTime), ToHhMm(r.CheckOutTime)));
                }
            }
        }

        var locList = new List<RtmLocationAttendance>();
        foreach (var loc in plan.Locations)
        {
            var days = new List<RtmAttendanceDay>();
            var openDays = 0;
            var presenceDays = 0;

            foreach (var day in loc.Days)
            {
                if (day.Status == "CLOSED")
                {
                    days.Add(new RtmAttendanceDay(day.Date, "CLOSED",
                        new List<RtmPresentAgent>(), new List<string>()));
                    continue;
                }

                openDays++;
                var key = (day.Date, loc.LocationCode);
                present.TryGetValue(key, out var presList);
                var presList2 = presList ?? new List<(string, string, string?, string?)>();
                var presentDtos = presList2
                    .Select(p => new RtmPresentAgent(p.Emp, p.Name, p.In, p.Out))
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var presentIds = presList2.Select(p => p.Emp).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var plannedAbsent = new List<string>();
                if (plannedNames.TryGetValue(key, out var names))
                    foreach (var (emp, name) in names)
                        if (!presentIds.Contains(emp))
                            plannedAbsent.Add(name);
                plannedAbsent.Sort(StringComparer.OrdinalIgnoreCase);

                var status = presentDtos.Count == 0 ? "NOT_PRESENT" : "PRESENT";
                if (presentDtos.Count > 0) presenceDays++;
                days.Add(new RtmAttendanceDay(day.Date, status, presentDtos, plannedAbsent));
            }

            locList.Add(new RtmLocationAttendance(
                loc.LocationCode, loc.City, loc.DisplayName, loc.Country,
                openDays, presenceDays, days));
        }

        return new RtmAttendanceResponse(monday.ToString("yyyy-MM-dd"), friday.ToString("yyyy-MM-dd"), locList);
    }

    /// <summary>
    /// Maps the kiosk's <c>wic_centers.location</c> string ("DE~&lt;PLZ&gt;~&lt;City&gt;~&lt;Street&gt;")
    /// to a <see cref="WicLocation.LocationCode"/> by the postal code embedded in both strings.
    /// Where two centres share a postal code (Demmin), the street disambiguates. Returns null
    /// when the centre cannot be resolved — the caller then falls back to the planned location.
    /// </summary>
    private static string? MapKioskCenterToLocation(string? kioskLocation,
        Dictionary<string, List<WicLocation>> byPostal)
    {
        if (string.IsNullOrWhiteSpace(kioskLocation)) return null;
        var plz = PlzOf(kioskLocation);
        var street = StreetOf(kioskLocation);
        if (plz == null || !byPostal.TryGetValue(plz, out var candidates) || candidates.Count == 0)
            return null;
        if (candidates.Count == 1) return candidates[0].LocationCode;
        if (street != null)
            foreach (var c in candidates)
            {
                var locStreet = StreetOf(c.LocationCode);
                if (locStreet != null &&
                    (locStreet.Contains(street, StringComparison.OrdinalIgnoreCase) ||
                     street.Contains(locStreet, StringComparison.OrdinalIgnoreCase)))
                    return c.LocationCode;
            }
        return null; // ambiguous → fall back to planned
    }

    private static string? PlzOf(string? locationCode)
    {
        if (string.IsNullOrWhiteSpace(locationCode)) return null;
        var parts = locationCode.Split('~');
        return parts.Length >= 2 ? parts[1].Trim() : null;
    }

    private static string? StreetOf(string? locationCode)
    {
        if (string.IsNullOrWhiteSpace(locationCode)) return null;
        var parts = locationCode.Split('~');
        return parts.Length >= 4 ? parts[3].Trim() : null;
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

                var from = last.PeriodFrom != default ? last.PeriodFrom : today;
                var nowPlan = await LoadPlanAsync(from, last.PeriodTo);
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

    public const int WeeklyDays = 14;

    private static readonly XLColor HeaderBlue     = XLColor.FromHtml("#1F4E79");
    private static readonly XLColor ClosedBg       = XLColor.FromHtml("#E7E6E6");
    private static readonly XLColor UncoveredBg    = XLColor.FromHtml("#F8CBAD");
    private static readonly XLColor AltRowBg       = XLColor.FromHtml("#DDEBF7");
    private static readonly XLColor CorrectionBg   = XLColor.FromHtml("#FFF2CC");
    private static readonly XLColor BorderGray     = XLColor.FromHtml("#BFBFBF");
    private static readonly XLColor ClosedFont     = XLColor.FromHtml("#595959");
    private static readonly XLColor UncoveredFont  = XLColor.FromHtml("#C00000");

    public async Task<byte[]> ExportWeeklyToExcelAsync(DateOnly start, string? actor)
    {
        var to = start.AddDays(WeeklyDays - 1);
        var plan = await LoadPlanAsync(start, to);
        var colDefs = DayColumns(start, VisibleDates(plan.Locations, start, to));

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("WIC Weekly Forecast");
        int lastCol = 1 + colDefs.Count;
        ApplyTitle(ws, "WIC Weekly Forecast", start, to, actor, lastCol);

        const int headerRow = 3;
        ws.Cell(headerRow, 1).Value = "Location";
        for (var i = 0; i < colDefs.Count; i++)
            if (colDefs[i] is DateOnly d) ws.Cell(headerRow, i + 2).Value = DayHeader(d);
        StyleHeaderRow(ws, headerRow, lastCol);

        var r = headerRow + 1;
        var parity = 0;
        foreach (var loc in plan.Locations)
        {
            var locCell = ws.Cell(r, 1);
            locCell.Value = LocationLabel(loc.LocationCode, loc.City, loc.DisplayName);
            locCell.Style.Font.Bold = true;
            locCell.Style.Alignment.WrapText = true;
            locCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

            var maxLines = VisualLines(LocationLabel(loc.LocationCode, loc.City, loc.DisplayName), 32);
            for (var i = 0; i < colDefs.Count; i++)
            {
                var cell = ws.Cell(r, i + 2);
                if (colDefs[i] is not DateOnly d)
                {
                    cell.Style.Fill.BackgroundColor = BorderGray;
                    continue;
                }
                var day = loc.Days.FirstOrDefault(x => x.Date == d.ToString("yyyy-MM-dd"))
                          ?? new RtmPlanDay(d.ToString("yyyy-MM-dd"), "CLOSED", new List<RtmPlanAgent>());
                SetPlanCell(cell, day, parity);
                maxLines = Math.Max(maxLines, VisualLines(PlanCellText(day), 22));
            }
            ws.Row(r).Height = Math.Max(22, maxLines * 14 + 4);
            r++;
            parity++;
        }

        ws.Column(1).Width = 34;
        for (var i = 0; i < colDefs.Count; i++)
            ws.Column(i + 2).Width = colDefs[i] is DateOnly ? 24 : 2;

        ThinBorders(ws, headerRow, r - 1, lastCol);
        ApplyTableSetup(ws, headerRow, lastCol, r - 1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportLastWeekToExcelAsync(DateOnly monday, string? actor)
    {
        var att = await GetLastWeekAsync(monday);
        var friday = monday.AddDays(4);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("WIC Last Week Attendance");
        const int lastCol = 8;
        ApplyTitle(ws, "WIC Last Week Attendance", monday, friday, actor, lastCol);

        const int headerRow = 3;
        ws.Cell(headerRow, 1).Value = "Location";
        for (var i = 0; i < 5; i++)
            ws.Cell(headerRow, i + 2).Value = DayHeader(monday.AddDays(i));
        ws.Cell(headerRow, 7).Value = "Open days";
        ws.Cell(headerRow, 8).Value = "Days with presence";
        StyleHeaderRow(ws, headerRow, lastCol);

        var r = headerRow + 1;
        var parity = 0;
        foreach (var loc in att.Locations)
        {
            var locCell = ws.Cell(r, 1);
            locCell.Value = LocationLabel(loc.LocationCode, loc.City, loc.DisplayName);
            locCell.Style.Font.Bold = true;
            locCell.Style.Alignment.WrapText = true;
            locCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

            var maxLines = VisualLines(LocationLabel(loc.LocationCode, loc.City, loc.DisplayName), 32);
            for (var i = 0; i < loc.Days.Count && i < 5; i++)
            {
                var cell = ws.Cell(r, i + 2);
                SetAttendanceCell(cell, loc.Days[i], parity);
                maxLines = Math.Max(maxLines, VisualLines(AttendanceCellText(loc.Days[i]), 22));
            }
            ws.Cell(r, 7).Value = loc.OpenDays;
            ws.Cell(r, 8).Value = loc.DaysWithPresence;
            ws.Cell(r, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(r, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(r).Height = Math.Max(22, maxLines * 14 + 4);
            r++;
            parity++;
        }

        ws.Column(1).Width = 34;
        for (var i = 2; i <= 6; i++) ws.Column(i).Width = 24;
        ws.Column(7).Width = 10;
        ws.Column(8).Width = 16;

        ThinBorders(ws, headerRow, r - 1, lastCol);
        ApplyTableSetup(ws, headerRow, lastCol, r - 1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportCorrectionsToExcelAsync(RtmCorrectionsResponse corr, string? actor,
        DateOnly periodFrom, DateOnly periodTo)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("WIC Daily Corrections");
        const int lastCol = 4;
        ApplyTitle(ws, "WIC Daily Corrections", periodFrom, periodTo, actor, lastCol);

        const int headerRow = 3;
        var headers = new[] { "Date", "Location", "Before", "Now" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeaderRow(ws, headerRow, lastCol);

        var r = headerRow + 1;
        foreach (var c in corr.Changes)
        {
            var dateCell = ws.Cell(r, 1);
            dateCell.Value = c.Date;
            dateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var locCell = ws.Cell(r, 2);
            locCell.Value = c.Location;
            locCell.Style.Font.Bold = true;

            var beforeCell = ws.Cell(r, 3);
            beforeCell.Value = c.Before;
            beforeCell.Style.Alignment.WrapText = true;
            beforeCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

            var nowCell = ws.Cell(r, 4);
            nowCell.Value = c.Now;
            nowCell.Style.Alignment.WrapText = true;
            nowCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            nowCell.Style.Fill.BackgroundColor = CorrectionBg;

            ws.Row(r).Height = Math.Max(22, Math.Max(VisualLines(c.Before, 34), VisualLines(c.Now, 34)) * 14 + 4);
            r++;
        }

        ws.Column(1).Width = 12;
        ws.Column(2).Width = 34;
        ws.Column(3).Width = 34;
        ws.Column(4).Width = 34;

        ThinBorders(ws, headerRow, Math.Max(r - 1, headerRow), lastCol);
        ApplyTableSetup(ws, headerRow, lastCol, Math.Max(r - 1, headerRow));

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
        d.ToString("ddd dd.MM.", CultureInfo.InvariantCulture);

    private static void ApplyTitle(IXLWorksheet ws, string title, DateOnly from, DateOnly to, string? actor, int lastCol)
    {
        var t = ws.Cell(1, 1);
        t.Value = title;
        t.Style.Font.Bold = true;
        t.Style.Font.FontSize = 14;
        if (lastCol > 1) ws.Range(1, 1, 1, lastCol).Merge();

        var p = ws.Cell(2, 1);
        p.Value = $"{from:dd.MM.yyyy} – {to:dd.MM.yyyy}";
        p.Style.Font.Bold = true;

        var g = ws.Cell(2, 2);
        g.Value = $"Generated: {DateTime.Now:dd.MM.yyyy HH:mm} by {actor ?? "—"}";
        g.Style.Font.FontColor = ClosedFont;
        g.Style.Font.FontSize = 10;
        if (lastCol > 2)
        {
            ws.Range(2, 2, 2, lastCol).Merge();
            ws.Range(2, 2, 2, lastCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }
    }

    private static void StyleHeaderRow(IXLWorksheet ws, int row, int lastCol)
    {
        for (var i = 1; i <= lastCol; i++)
        {
            var c = ws.Cell(row, i);
            c.Style.Font.Bold = true;
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Fill.BackgroundColor = HeaderBlue;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            c.Style.Alignment.WrapText = true;
        }
        ws.Row(row).Height = 22;
    }

    private static void SetPlanCell(IXLCell cell, RtmPlanDay day, int parity)
    {
        cell.Style.Alignment.WrapText = true;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        cell.Value = PlanCellText(day);
        switch (day.Status)
        {
            case "CLOSED":
                cell.Style.Fill.BackgroundColor = ClosedBg;
                cell.Style.Font.FontColor = ClosedFont;
                break;
            case "UNCOVERED":
                cell.Style.Fill.BackgroundColor = UncoveredBg;
                cell.Style.Font.FontColor = UncoveredFont;
                cell.Style.Font.Bold = true;
                break;
            default:
                cell.Style.Fill.BackgroundColor = parity % 2 == 0 ? XLColor.White : AltRowBg;
                break;
        }
    }

    private static void SetAttendanceCell(IXLCell cell, RtmAttendanceDay day, int parity)
    {
        cell.Style.Alignment.WrapText = true;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        if (day.Status == "CLOSED")
        {
            cell.Value = "Closed";
            cell.Style.Fill.BackgroundColor = ClosedBg;
            cell.Style.Font.FontColor = ClosedFont;
            return;
        }
        if (day.Status == "NOT_PRESENT")
        {
            cell.Value = "Not present";
            cell.Style.Fill.BackgroundColor = UncoveredBg;
            cell.Style.Font.FontColor = UncoveredFont;
            cell.Style.Font.Bold = true;
            return;
        }
        cell.Style.Fill.BackgroundColor = parity % 2 == 0 ? XLColor.White : AltRowBg;
        var rt = cell.CreateRichText();
        var first = true;
        foreach (var p in day.Present)
        {
            if (!first) rt.AddNewLine();
            var time = p.CheckIn != null ? (p.CheckOut != null ? $"{p.CheckIn}–{p.CheckOut}" : p.CheckIn) : null;
            rt.AddText(time != null ? $"{p.Name} {time}" : p.Name);
            first = false;
        }
        foreach (var n in day.PlannedAbsent)
        {
            if (!first) rt.AddNewLine();
            rt.AddText($"Planned: {n}").SetItalic().SetFontColor(ClosedFont);
            first = false;
        }
    }

    private static void ThinBorders(IXLWorksheet ws, int firstRow, int lastRow, int lastCol)
    {
        if (lastRow < firstRow) return;
        var range = ws.Range(firstRow, 1, lastRow, lastCol);
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorderColor = BorderGray;
        range.Style.Border.OutsideBorderColor = BorderGray;
    }

    private static void ApplyTableSetup(IXLWorksheet ws, int headerRow, int lastCol, int lastRow)
    {
        ws.SheetView.Freeze(headerRow, 1);
        ws.Range(headerRow, 1, headerRow, lastCol).SetAutoFilter();
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(1, headerRow);
    }

    private static int VisualLines(string? text, int charsPerLine)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        var lines = 0;
        foreach (var line in text.Split('\n'))
            lines += Math.Max(1, (int)Math.Ceiling(line.Length / (double)Math.Max(1, charsPerLine)));
        return Math.Max(1, lines);
    }

    private static string LocationLabel(string locationCode, string? city, string displayName)
    {
        var street = StreetOf(locationCode);
        var c = string.IsNullOrWhiteSpace(city) ? displayName : city;
        return string.IsNullOrWhiteSpace(street) ? c : $"{c} – {street}";
    }

    private static List<DateOnly> VisibleDates(List<RtmLocationPlan> locations, DateOnly from, DateOnly to)
    {
        var result = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) { result.Add(d); continue; }
            var ds = d.ToString("yyyy-MM-dd");
            if (locations.Any(loc => loc.Days.Any(x => x.Date == ds && x.Status != "CLOSED")))
                result.Add(d);
        }
        return result;
    }

    // Column descriptors for the weekly forecast: null = the week-1/week-2 separator band.
    private static List<DateOnly?> DayColumns(DateOnly start, List<DateOnly> visible)
    {
        var result = new List<DateOnly?>();
        var week2 = start.AddDays(7);
        var inserted = false;
        foreach (var d in visible)
        {
            if (!inserted && d >= week2) { result.Add(null); inserted = true; }
            result.Add(d);
        }
        return result;
    }

    public static DateOnly NextMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (days == 0) days = 7;
        return today.AddDays(days);
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
                    GetStr(el, "work_location"),
                    GetStr(el, "day_work_location"),
                    GetStr(el, "day_wic_center_location"));
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
        string? CheckInTime, string? CheckOutTime, string? WorkLocation,
        string? DayWorkLocation, string? DayWicCenterLocation);
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
                ? d : RtmReportService.NextMonday();
            var plan = await svc.GetPlanAsync(from, from.AddDays(RtmReportService.WeeklyDays - 1));
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
                ? d : RtmReportService.NextMonday();
            var to = from.AddDays(RtmReportService.WeeklyDays - 1);
            var bytes = await svc.ExportWeeklyToExcelAsync(from, ResolveActor(ctx));
            return Results.File(bytes, ExcelHeaders(ctx, $"WIC_Forecast_{from:yyyy-MM-dd}_to_{to:yyyy-MM-dd}.xlsx"));
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
            var to = from.AddDays(4);
            var bytes = await svc.ExportLastWeekToExcelAsync(from, ResolveActor(ctx));
            return Results.File(bytes, ExcelHeaders(ctx, $"WIC_Attendance_{from:yyyy-MM-dd}_to_{to:yyyy-MM-dd}.xlsx"));
        });

        grp.MapGet("/download/corrections", async (RtmReportService svc, HttpContext ctx) =>
        {
            if (Deny(ctx) is { } deny) return deny;
            var corr = await svc.GetCorrectionsAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var from = DateOnly.TryParse(corr.LastSent?.PeriodFrom, out var f) ? f : today;
            var to = DateOnly.TryParse(corr.LastSent?.PeriodTo, out var t2) ? t2 : today;
            var bytes = await svc.ExportCorrectionsToExcelAsync(corr, ResolveActor(ctx), from, to);
            return Results.File(bytes, ExcelHeaders(ctx, $"WIC_Corrections_{from:yyyy-MM-dd}_to_{to:yyyy-MM-dd}.xlsx"));
        });
    }
}
