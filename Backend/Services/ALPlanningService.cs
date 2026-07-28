using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

// ─── Request / Response DTOs ─────────────────────────────────────────────────

public record ALDateRange(string From, string To);

public record ALPlanningRequest(
    string EmployeeId,
    List<ALDateRange> DateRanges
);

public record ALBestSubstitute(
    string EmployeeId,
    string FullName,
    string SourceType,
    double? DistanceKm,
    string? ReachabilityTier
);

public record ALConflict(
    string EmployeeId,
    string FullName,
    string AbsenceType,
    string LocationCode,
    string LocationName
);

public record ALLocationDay(
    string LocationCode,
    string DisplayName,
    string CoverageStatus,
    double Present,
    int Required,
    double Gap,
    ALBestSubstitute? BestSubstitute
);

public record ALDayResult(
    string Date,
    string DayOfWeek,
    List<ALLocationDay> Locations,
    List<ALConflict> Conflicts
);

public record ALRangeResult(
    string From,
    string To,
    int TotalDays,
    int AtRiskDays,
    List<ALDayResult> Days
);

public record ALPlanningResponse(
    string EmployeeId,
    string? FullName,
    string GeneratedAt,
    List<ALRangeResult> DateRanges,
    string Note
);

// ─── Service ─────────────────────────────────────────────────────────────────

public class ALPlanningService
{
    private readonly GSDContext _db;
    private readonly WhatIfService _whatIf;

    public ALPlanningService(GSDContext db, WhatIfService whatIf)
    {
        _db = db;
        _whatIf = whatIf;
    }

    public async Task<ALPlanningResponse?> PlanAsync(ALPlanningRequest req)
    {
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.IsActive);
        if (employee == null) return null;

        var rangeResults = new List<ALRangeResult>();
        string note = "Simulation only — no changes made to the schedule.";

        foreach (var range in req.DateRanges)
        {
            if (!DateOnly.TryParse(range.From, out var fromDate) ||
                !DateOnly.TryParse(range.To,   out var toDate))
                continue;
            if (toDate < fromDate) toDate = fromDate;

            int totalDays = toDate.DayNumber - fromDate.DayNumber + 1;
            int horizon   = Math.Min(totalDays, 14);

            var whatIf = await _whatIf.SimulateAbsenceAsync(
                req.EmployeeId, fromDate.ToString("yyyy-MM-dd"), horizon);

            if (whatIf != null && whatIf.AffectedLocations.Count == 0)
                note = "Employee has no active MAIN WIC assignments. AL has no direct WIC coverage impact.";

            var dayResults = new List<ALDayResult>();
            int atRiskDays = 0;

            for (int d = 0; d < horizon; d++)
            {
                var date    = fromDate.AddDays(d);
                var dateStr = date.ToString("yyyy-MM-dd");

                var locationDays = new List<ALLocationDay>();

                foreach (var loc in whatIf?.AffectedLocations ?? [])
                {
                    if (loc.Substitution == null) continue;
                    var dayData = loc.Substitution.Days.FirstOrDefault(x => x.Date == dateStr);
                    if (dayData == null) continue;

                    ALBestSubstitute? best = null;
                    if (dayData.BestPickId != null)
                    {
                        var bc = dayData.Candidates.FirstOrDefault(c => c.EmployeeId == dayData.BestPickId);
                        if (bc != null)
                            best = new ALBestSubstitute(
                                bc.EmployeeId, bc.FullName, bc.SourceType,
                                bc.DistanceKm, bc.ReachabilityTier);
                    }

                    locationDays.Add(new ALLocationDay(
                        loc.LocationCode, loc.DisplayName, dayData.CurrentStatus,
                        dayData.Present, dayData.Required, dayData.Gap, best));
                }

                bool isAtRisk = locationDays.Any(l => l.CoverageStatus is "UNCOVERED" or "PARTIAL");
                if (isAtRisk) atRiskDays++;

                var affectedCodes = locationDays.Select(l => l.LocationCode).ToList();
                var conflicts = await GetConflictsAsync(affectedCodes, date, req.EmployeeId);

                dayResults.Add(new ALDayResult(dateStr, date.DayOfWeek.ToString(),
                    locationDays, conflicts));
            }

            rangeResults.Add(new ALRangeResult(
                fromDate.ToString("yyyy-MM-dd"),
                fromDate.AddDays(horizon - 1).ToString("yyyy-MM-dd"),
                totalDays, atRiskDays, dayResults));
        }

        return new ALPlanningResponse(
            req.EmployeeId, employee.FullName,
            DateTime.UtcNow.ToString("o"), rangeResults, note);
    }

    private async Task<List<ALConflict>> GetConflictsAsync(
        List<string> locationCodes, DateOnly date, string excludeEmployeeId)
    {
        if (locationCodes.Count == 0) return [];

        var locations = await _db.WicLocations
            .Where(l => locationCodes.Contains(l.LocationCode) && l.IsActive)
            .ToListAsync();

        // Include legacy codes so assignments stored with old-style codes are also found
        var allCodes = locations
            .SelectMany(l => new[] { l.LocationCode, l.LocationCodeLegacy }
                .Where(c => c != null).Select(c => c!))
            .ToList();

        var assignments = await _db.WicAgentAssignments
            .Where(a => allCodes.Contains(a.LocationCode) && a.AssignmentType == "MAIN" && a.IsActive)
            .ToListAsync();

        var empNames = assignments.Select(a => a.EmployeeName).Distinct().ToList();

        var employees = await _db.Employees
            .Where(e => e.FullName != null && empNames.Contains(e.FullName!)
                     && e.EmployeeId != excludeEmployeeId && e.IsActive)
            .ToListAsync();

        var empIds = employees.Select(e => e.EmployeeId).ToList();
        if (empIds.Count == 0) return [];

        var shiftAbsences = await _db.ShiftEntries
            .Where(s => empIds.Contains(s.EmployeeId) && s.ShiftDate == date
                     && (s.ShiftType == "AL" || s.ShiftType == "SL" || s.ShiftType == "UL"
                      || s.ShiftType == "HALF_AL" || s.ShiftType == "TRAINING"))
            .ToListAsync();

        var sickAbsences = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && empIds.Contains(sl.EmployeeId!)
                      && sl.FirstDay <= date && sl.LastDay >= date)
            .ToListAsync();

        // GroupBy guards against duplicate rows for the same employee+date (e.g. WORKING
        // from EXCEL + AL from AL_IMPORT). BestShiftEntry picks highest Id (latest correction).
        var shiftByEmpId = shiftAbsences
            .GroupBy(s => s.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g),
                StringComparer.OrdinalIgnoreCase);
        var sickEmpIds = sickAbsences
            .Where(sl => sl.EmployeeId != null)
            .Select(sl => sl.EmployeeId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Assignment code -> display name (prefer new-style match)
        var locDisplayByCode = locations
            .ToDictionary(l => l.LocationCode, l => l.DisplayName ?? l.LocationCode,
                StringComparer.OrdinalIgnoreCase);

        var locCodeByName = assignments
            .GroupBy(a => a.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().LocationCode, StringComparer.OrdinalIgnoreCase);

        var conflicts = new List<ALConflict>();
        foreach (var emp in employees)
        {
            bool hasShift = shiftByEmpId.ContainsKey(emp.EmployeeId);
            bool hasSick  = sickEmpIds.Contains(emp.EmployeeId);
            if (!hasShift && !hasSick) continue;

            string absType = hasShift ? shiftByEmpId[emp.EmployeeId].ShiftType : "SL";

            string assignedCode = locCodeByName.TryGetValue(emp.FullName ?? "", out var lc)
                ? lc : locationCodes.FirstOrDefault() ?? "";

            // Resolve legacy code to new-style
            var matchedLoc = locations.FirstOrDefault(l =>
                string.Equals(l.LocationCode, assignedCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.LocationCodeLegacy, assignedCode, StringComparison.OrdinalIgnoreCase));
            string resolvedCode    = matchedLoc?.LocationCode ?? assignedCode;
            string resolvedDisplay = matchedLoc?.DisplayName  ?? assignedCode;

            conflicts.Add(new ALConflict(
                emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                absType, resolvedCode, resolvedDisplay));
        }

        return conflicts;
    }
}

// ─── Endpoint mapper ─────────────────────────────────────────────────────────

public static class ALPlanningEndpointMapper
{
    public static void MapALPlanningEndpoints(this WebApplication app)
    {
        app.MapPost("/api/wic/al-planning", async (
            ALPlanningRequest req,
            ALPlanningService svc) =>
        {
            if (string.IsNullOrEmpty(req.EmployeeId))
                return Results.BadRequest(new { error = "EmployeeId is required." });
            if (req.DateRanges == null || req.DateRanges.Count == 0)
                return Results.BadRequest(new { error = "At least one date range is required." });
            if (req.DateRanges.Count > 10)
                return Results.BadRequest(new { error = "Maximum 10 date ranges per request." });

            var result = await svc.PlanAsync(req);
            return result == null
                ? Results.NotFound(new { error = $"Employee '{req.EmployeeId}' not found or inactive." })
                : Results.Ok(result);
        }).WithTags("WIC");
    }
}
