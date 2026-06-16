using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public record WhatIfLocationResult(
    string LocationCode,
    string DisplayName,
    string AssignmentType,
    SubstitutionResponse? Substitution
);

public record WhatIfResponse(
    string EmployeeId,
    string? FullName,
    string StartDate,
    int Horizon,
    string Note,
    List<WhatIfLocationResult> AffectedLocations
);

/// <summary>
/// Hypothetical absence simulation — no DB writes.
/// Finds all WIC locations where the employee is MAIN and calls SubstitutionService
/// with the employee marked absent, returning what coverage would look like.
/// </summary>
public class WhatIfService
{
    private readonly GSDContext _db;
    private readonly SubstitutionService _substitution;

    public WhatIfService(GSDContext db, SubstitutionService substitution)
    {
        _db = db;
        _substitution = substitution;
    }

    public async Task<WhatIfResponse?> SimulateAbsenceAsync(
        string employeeId,
        string? dateStr,
        int horizon = 5)
    {
        horizon = Math.Clamp(horizon, 1, 14);
        var startDate = dateStr != null && DateOnly.TryParse(dateStr, out var pd)
            ? pd : DateOnly.FromDateTime(DateTime.Today);

        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId && e.IsActive);
        if (employee == null) return null;

        // Find all WIC locations where this employee is MAIN (by FullName match)
        var allLocations = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var allAssignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();

        var mainAssignments = allAssignments
            .Where(a => a.AssignmentType == "MAIN" &&
                        string.Equals(a.EmployeeName, employee.FullName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Resolve assignment codes to new-style location codes
        var locByCode = allLocations.ToDictionary(l => l.LocationCode, StringComparer.OrdinalIgnoreCase);
        var locByLegacy = allLocations
            .Where(l => l.LocationCodeLegacy != null)
            .ToDictionary(l => l.LocationCodeLegacy!, StringComparer.OrdinalIgnoreCase);

        var affectedLocationCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in mainAssignments)
        {
            if (locByCode.TryGetValue(a.LocationCode, out var loc))
                affectedLocationCodes.Add(loc.LocationCode);
            else if (locByLegacy.TryGetValue(a.LocationCode, out var locByLeg))
                affectedLocationCodes.Add(locByLeg.LocationCode);
        }

        if (affectedLocationCodes.Count == 0)
        {
            return new WhatIfResponse(
                employeeId, employee.FullName, startDate.ToString("yyyy-MM-dd"), horizon,
                "Employee has no MAIN WIC assignments — absence has no direct WIC impact.",
                new List<WhatIfLocationResult>());
        }

        var absentIds = new List<string> { employeeId };
        var results = new List<WhatIfLocationResult>();

        foreach (var locCode in affectedLocationCodes.OrderBy(c => c))
        {
            var loc = locByCode.TryGetValue(locCode, out var l) ? l : null;
            if (loc == null) continue;

            var substitution = await _substitution.GetSubstitutesAsync(
                locCode, startDate.ToString("yyyy-MM-dd"), horizon, absentIds);

            results.Add(new WhatIfLocationResult(
                locCode, loc.DisplayName, "MAIN", substitution));
        }

        return new WhatIfResponse(
            employeeId, employee.FullName,
            startDate.ToString("yyyy-MM-dd"), horizon,
            "Hypothetical simulation only — no DB changes made.",
            results);
    }
}

public static class WhatIfEndpointMapper
{
    public static void MapWhatIfEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wic/whatif", async (
            string absentEmployeeId,
            string? date,
            int? horizon,
            WhatIfService svc) =>
        {
            var result = await svc.SimulateAbsenceAsync(absentEmployeeId, date, horizon ?? 5);
            return result == null
                ? Results.NotFound(new { error = $"Employee '{absentEmployeeId}' not found or inactive." })
                : Results.Ok(result);
        }).WithTags("WIC");
    }
}
