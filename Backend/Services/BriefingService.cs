using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public record BriefingAbsenceDto(
    string EmployeeId,
    string? FullName,
    string? TeamLead,
    string ShiftType,
    string? WicLocation   // DisplayName of their primary WIC assignment (if any)
);

public record BriefingGapDto(
    string LocationCode,
    string DisplayName,
    int Present,
    int Required,
    double Gap,
    string Status,
    string? BestSubstituteId,
    string? BestSubstituteName,
    string? BestSubstituteSource
);

public record BriefingAtRiskDto(
    string Date,
    string DayOfWeek,
    string LocationCode,
    string DisplayName,
    string Status,
    int Present,
    int Required
);

public record BriefingResponse(
    string Date,
    int TotalAbsences,
    int TotalGaps,
    List<BriefingAbsenceDto> Absences,
    List<BriefingGapDto> Gaps,
    List<BriefingAtRiskDto> NextAtRiskDays  // first 3 AT_RISK days in next 14 days
);

public class BriefingService
{
    private readonly GSDContext _db;
    private readonly SubstitutionService _substitution;
    private const int AtRiskLookahead = 14;

    private static readonly HashSet<string> _fullAbsenceTypes =
        new(StringComparer.OrdinalIgnoreCase) { "SL", "AL", "UL", "PH", "LPH", "RESIGNED" };

    public BriefingService(GSDContext db, SubstitutionService substitution)
    {
        _db = db;
        _substitution = substitution;
    }

    public async Task<BriefingResponse> GetBriefingAsync(string? dateStr)
    {
        var date = dateStr != null && DateOnly.TryParse(dateStr, out var pd)
            ? pd : DateOnly.FromDateTime(DateTime.Today);
        var lookaheadEnd = date.AddDays(AtRiskLookahead);

        // ── Reference data ────────────────────────────────────────────────────
        var locations = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var allHours  = await _db.WicOpeningHours.ToListAsync();
        var publicHolidays = await _db.PublicHolidays
            .Where(ph => ph.HolidayDate >= date && ph.HolidayDate <= lookaheadEnd)
            .ToListAsync();
        var allAssignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();
        var allEmployees   = await _db.Employees.Where(e => e.IsActive).ToListAsync();

        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= date && w.ShiftDate <= lookaheadEnd)
            .ToListAsync();
        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= date && s.ShiftDate <= lookaheadEnd)
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => g.First());

        // ── Absences today ────────────────────────────────────────────────────
        var todayShifts = shiftEntries.Where(s => s.ShiftDate == date).ToList();
        var absenceShifts = todayShifts
            .Where(s => _fullAbsenceTypes.Contains(s.ShiftType) ||
                        string.Equals(s.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var empById = allEmployees.ToDictionary(e => e.EmployeeId, StringComparer.OrdinalIgnoreCase);

        // Build WIC location lookup per employee name (for absence -> WIC link)
        var mainWicByEmpName = allAssignments
            .Where(a => a.AssignmentType == "MAIN")
            .GroupBy(a => a.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var locByCode = locations.ToDictionary(l => l.LocationCode, StringComparer.OrdinalIgnoreCase);
        var locByLegacy = locations.Where(l => l.LocationCodeLegacy != null)
            .ToDictionary(l => l.LocationCodeLegacy!, StringComparer.OrdinalIgnoreCase);

        string? ResolveWicDisplay(string empId)
        {
            if (!empById.TryGetValue(empId, out var emp) || emp.FullName == null) return null;
            if (!mainWicByEmpName.TryGetValue(emp.FullName, out var assign)) return null;
            if (locByCode.TryGetValue(assign.LocationCode, out var loc)) return loc.DisplayName;
            if (locByLegacy.TryGetValue(assign.LocationCode, out var locL)) return locL.DisplayName;
            return null;
        }

        var absences = absenceShifts.Select(s =>
        {
            empById.TryGetValue(s.EmployeeId, out var emp);
            return new BriefingAbsenceDto(
                s.EmployeeId,
                emp?.FullName,
                emp?.TeamLeadName,
                s.ShiftType,
                ResolveWicDisplay(s.EmployeeId));
        }).OrderBy(a => a.FullName).ToList();

        // ── Gaps today ────────────────────────────────────────────────────────
        int todayDow = (int)date.DayOfWeek;
        bool isNationalToday = publicHolidays.Any(ph => ph.HolidayDate == date && ph.IsNational);

        var gapList = new List<BriefingGapDto>();

        foreach (var loc in locations)
        {
            var hours = allHours.FirstOrDefault(h => h.LocationCode == loc.LocationCode && h.DayOfWeek == todayDow);
            string? bundesland = loc.Bundesland ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country);
            bool isRegional = bundesland != null && publicHolidays.Any(ph =>
                ph.HolidayDate == date && string.Equals(ph.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase));
            bool isClosed = hours == null || hours.IsClosed || isNationalToday || isRegional;
            if (isClosed) continue;

            int minReq = loc.MinAgentsRequired ?? 1;
            var dayWic = wicEntries
                .Where(w => w.ShiftDate == date && w.IsOnSite && WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc))
                .ToList();

            double presentDouble = 0;
            foreach (var w in dayWic)
            {
                shiftByEmpDate.TryGetValue((w.EmployeeId, date), out var sh);
                if (sh != null && _fullAbsenceTypes.Contains(sh.ShiftType)) continue;
                presentDouble += (sh != null && string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase))
                    ? 0.5 : 1.0;
            }

            int effectiveCoverage = (int)Math.Floor(presentDouble);
            var evalResult = CoverageEvaluator.Classify(false, effectiveCoverage, minReq);
            if (evalResult.Status is CoverageStatus.COVERED) continue; // no gap

            double gap = Math.Max(0, minReq - presentDouble);

            // Get best substitute from SubstitutionService
            string? bestId = null, bestName = null, bestSource = null;
            try
            {
                var subResult = await _substitution.GetSubstitutesAsync(loc.LocationCode, date.ToString("yyyy-MM-dd"), 1);
                var day0 = subResult?.Days.FirstOrDefault();
                if (day0?.BestPickId != null)
                {
                    bestId = day0.BestPickId;
                    var bestCand = day0.Candidates.FirstOrDefault(c => c.EmployeeId == bestId);
                    bestName = bestCand?.FullName;
                    bestSource = bestCand?.SourceType;
                }
            }
            catch { }

            gapList.Add(new BriefingGapDto(
                loc.LocationCode, loc.DisplayName,
                effectiveCoverage, minReq, gap,
                evalResult.Status.ToString(),
                bestId, bestName, bestSource));
        }

        // ── Next AT_RISK days (lookahead, max 3) ──────────────────────────────
        var atRiskList = new List<BriefingAtRiskDto>();

        for (var scanDate = date.AddDays(1); scanDate <= lookaheadEnd && atRiskList.Count < 3; scanDate = scanDate.AddDays(1))
        {
            int scanDow = (int)scanDate.DayOfWeek;
            bool isNationalScan = publicHolidays.Any(ph => ph.HolidayDate == scanDate && ph.IsNational);

            foreach (var loc in locations)
            {
                if (atRiskList.Count >= 3) break;

                var hours = allHours.FirstOrDefault(h => h.LocationCode == loc.LocationCode && h.DayOfWeek == scanDow);
                string? bundesland = loc.Bundesland ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country);
                bool isRegional = bundesland != null && publicHolidays.Any(ph =>
                    ph.HolidayDate == scanDate && string.Equals(ph.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase));
                bool isClosed = hours == null || hours.IsClosed || isNationalScan || isRegional;
                if (isClosed) continue;

                int minReq = loc.MinAgentsRequired ?? 1;
                var dayWic = wicEntries
                    .Where(w => w.ShiftDate == scanDate && w.IsOnSite && WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc))
                    .ToList();

                double presentDouble = 0;
                foreach (var w in dayWic)
                {
                    shiftByEmpDate.TryGetValue((w.EmployeeId, scanDate), out var sh);
                    if (sh != null && _fullAbsenceTypes.Contains(sh.ShiftType)) continue;
                    presentDouble += (sh != null && string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase))
                        ? 0.5 : 1.0;
                }

                int effectiveCoverage = (int)Math.Floor(presentDouble);
                var evalResult = CoverageEvaluator.Classify(false, effectiveCoverage, minReq);
                if (evalResult.Status is CoverageStatus.UNCOVERED or CoverageStatus.PARTIAL)
                {
                    atRiskList.Add(new BriefingAtRiskDto(
                        scanDate.ToString("yyyy-MM-dd"), scanDate.DayOfWeek.ToString(),
                        loc.LocationCode, loc.DisplayName,
                        evalResult.Status.ToString(), effectiveCoverage, minReq));
                }
            }
        }

        return new BriefingResponse(
            date.ToString("yyyy-MM-dd"),
            absences.Count,
            gapList.Count,
            absences,
            gapList,
            atRiskList);
    }

    public async Task<byte[]> ExportBriefingToExcelAsync(string? dateStr)
    {
        var briefing = await GetBriefingAsync(dateStr);
        using var wb = new XLWorkbook();

        // Sheet 1: Absences
        var wsAbs = wb.Worksheets.Add("Absences");
        var absHeaders = new[] { "Employee ID", "Full Name", "Team Lead", "Shift Type", "WIC Location" };
        for (var i = 0; i < absHeaders.Length; i++)
        {
            wsAbs.Cell(1, i + 1).Value = absHeaders[i];
            wsAbs.Cell(1, i + 1).Style.Font.Bold = true;
            wsAbs.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            wsAbs.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < briefing.Absences.Count; r++)
        {
            var a = briefing.Absences[r]; var er = r + 2;
            wsAbs.Cell(er, 1).Value = a.EmployeeId;
            wsAbs.Cell(er, 2).Value = a.FullName ?? "";
            wsAbs.Cell(er, 3).Value = a.TeamLead ?? "";
            wsAbs.Cell(er, 4).Value = a.ShiftType;
            wsAbs.Cell(er, 5).Value = a.WicLocation ?? "";
        }
        wsAbs.Columns().AdjustToContents();

        // Sheet 2: Gaps
        var wsGap = wb.Worksheets.Add("Coverage Gaps");
        var gapHeaders = new[] { "Location Code", "Display Name", "Present", "Required", "Gap", "Status", "Best Substitute", "Source" };
        for (var i = 0; i < gapHeaders.Length; i++)
        {
            wsGap.Cell(1, i + 1).Value = gapHeaders[i];
            wsGap.Cell(1, i + 1).Style.Font.Bold = true;
            wsGap.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#d97706");
            wsGap.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < briefing.Gaps.Count; r++)
        {
            var g = briefing.Gaps[r]; var er = r + 2;
            wsGap.Cell(er, 1).Value = g.LocationCode;
            wsGap.Cell(er, 2).Value = g.DisplayName;
            wsGap.Cell(er, 3).Value = g.Present;
            wsGap.Cell(er, 4).Value = g.Required;
            wsGap.Cell(er, 5).Value = g.Gap;
            wsGap.Cell(er, 6).Value = g.Status;
            wsGap.Cell(er, 7).Value = g.BestSubstituteName ?? "";
            wsGap.Cell(er, 8).Value = g.BestSubstituteSource ?? "";
        }
        wsGap.Columns().AdjustToContents();

        // Sheet 3: Next AT_RISK Days
        var wsRisk = wb.Worksheets.Add("Next AT_RISK Days");
        var riskHeaders = new[] { "Date", "Day", "Location Code", "Display Name", "Status", "Present", "Required" };
        for (var i = 0; i < riskHeaders.Length; i++)
        {
            wsRisk.Cell(1, i + 1).Value = riskHeaders[i];
            wsRisk.Cell(1, i + 1).Style.Font.Bold = true;
            wsRisk.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#7c3aed");
            wsRisk.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < briefing.NextAtRiskDays.Count; r++)
        {
            var x = briefing.NextAtRiskDays[r]; var er = r + 2;
            wsRisk.Cell(er, 1).Value = x.Date;
            wsRisk.Cell(er, 2).Value = x.DayOfWeek;
            wsRisk.Cell(er, 3).Value = x.LocationCode;
            wsRisk.Cell(er, 4).Value = x.DisplayName;
            wsRisk.Cell(er, 5).Value = x.Status;
            wsRisk.Cell(er, 6).Value = x.Present;
            wsRisk.Cell(er, 7).Value = x.Required;
        }
        wsRisk.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

public static class BriefingEndpointMapper
{
    public static void MapBriefingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wic/briefing", async (string? date, BriefingService svc) =>
            Results.Ok(await svc.GetBriefingAsync(date))
        ).WithTags("WIC");

        app.MapGet("/api/wic/briefing/export", async (string? date, BriefingService svc, HttpContext ctx) =>
        {
            var bytes = await svc.ExportBriefingToExcelAsync(date);
            var filename = "WicBriefing_" + (date ?? DateTime.Today.ToString("yyyy-MM-dd")) + ".xlsx";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + filename + "\"";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }).WithTags("WIC");
    }
}
