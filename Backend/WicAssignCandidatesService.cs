using GSDDashboard.API.Data;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.WicShifts;

// ── Assign candidates (GET /api/wic/assign-candidates) ───────────────────────
// Feeds the "Assign" button added to the coverage-gaps banner and the WIC
// Shifts UNCOVERED cards: for a location+date, who should RTM actually pick.
// Ordered so the right choice is on top:
//   1. FLOATER  — available REGIONAL agents shared with this location
//   2. OWN      — the location's own MAIN/BACKUP agents, available first
//   3. OTHER    — everyone else (active employees)
// Absent candidates still appear (never hidden) but sort to the bottom of
// their own category with Available=false and the absence reason, so RTM can
// see why the usual person isn't an option instead of wondering where they
// went. Uses the exact same absence definition as WicCoverageGapService
// (WicCoverageGapService.AbsenceTypes) so the two features never disagree.
public record WicAssignCandidate(
    string EmployeeId, string FullName, string Category, string? AssignmentType,
    bool Available, string? AbsenceReason);

public record WicAssignCandidatesResult(
    string LocationCode, string DisplayName, string Date,
    List<WicAssignCandidate> Candidates, bool HasSensibleCandidate);

public class WicAssignCandidatesService
{
    private readonly GSDContext _db;
    public WicAssignCandidatesService(GSDContext db) => _db = db;

    public async Task<WicAssignCandidatesResult?> GetCandidatesAsync(string locationCode, DateOnly date)
    {
        var loc = await _db.WicLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode && l.IsActive);
        if (loc == null) return null;

        var allAssignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();
        var relevant = allAssignments.Where(a => WicLocationMatcher.MatchesAssignmentCode(a.LocationCode, loc)).ToList();
        var ownByName = relevant.Where(a => a.AssignmentType is "MAIN" or "BACKUP")
            .GroupBy(a => a.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().AssignmentType, StringComparer.OrdinalIgnoreCase);
        var floaterNames = relevant.Where(a => a.AssignmentType == "REGIONAL")
            .Select(a => a.EmployeeName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var employees = await _db.Employees.Where(e => e.IsActive).ToListAsync();
        var empIds = employees.Select(e => e.EmployeeId).ToList();
        var shiftsThatDay = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date && empIds.Contains(s.EmployeeId))
            .ToListAsync();
        var statusByEmp = shiftsThatDay
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g).ShiftType);

        // Agents who had already left by this day (inactive Employees row, or a RESIGNED
        // ShiftEntries row on/before it) are not candidates at all — not even as a
        // greyed-out row. IsActive alone removes the inactive ones (the query above);
        // the live Hannover case still had IsActive=1, so the shared historical signal is
        // applied here through the ONE rule (SubstituteEligibility.HasLeft) that the
        // coverage gaps, Find Substitute, the Overview top substitute and the Shift Plan
        // also use. A future exit date therefore keeps the agent offerable up to the day
        // before it (RESIGNED is dated on the exit day itself).
        var resignedRows = await _db.ShiftEntries
            .Where(s => s.ShiftType == SubstituteEligibility.Resigned && s.ShiftDate <= date)
            .ToListAsync();
        var resignedFrom = SubstituteEligibility.EarliestResignedByEmployee(resignedRows);

        var candidates = new List<WicAssignCandidate>();
        foreach (var e in employees)
        {
            if (SubstituteEligibility.HasLeft(e, date, resignedFrom)) continue;
            var name = e.FullName ?? e.EmployeeId;
            statusByEmp.TryGetValue(e.EmployeeId, out var status);
            bool isAbsent = status != null && WicCoverageGapService.AbsenceTypes.Contains(status);

            string category;
            string? assignmentType;
            if (ownByName.TryGetValue(name, out var ownType)) { category = "OWN"; assignmentType = ownType; }
            else if (floaterNames.Contains(name)) { category = "FLOATER"; assignmentType = "REGIONAL"; }
            else { category = "OTHER"; assignmentType = null; }

            candidates.Add(new WicAssignCandidate(
                e.EmployeeId, name, category, assignmentType,
                Available: !isAbsent, AbsenceReason: isAbsent ? status : null));
        }

        int CategoryRank(string c) => c switch { "FLOATER" => 0, "OWN" => 1, _ => 2 };
        candidates = candidates
            .OrderBy(c => CategoryRank(c.Category))
            .ThenByDescending(c => c.Available)
            .ThenBy(c => c.FullName)
            .ToList();

        bool hasSensible = candidates.Any(c => c.Category is "FLOATER" or "OWN" && c.Available);

        return new WicAssignCandidatesResult(
            loc.LocationCode, loc.DisplayName, date.ToString("yyyy-MM-dd"), candidates, hasSensible);
    }
}

public static class WicAssignCandidatesEndpointMapper
{
    public static void MapWicAssignCandidatesEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/wic").WithTags("WIC");

        // RTM / TEAM_LEAD / DEV only — same gate as /api/roster/missing and
        // /api/wic/coverage-gaps (RosterAccess).
        grp.MapGet("/assign-candidates", async (string? locationCode, string? date, HttpContext ctx, WicAssignCandidatesService svc) =>
        {
            if (RosterAccess.DenyResult(ctx) is { } deny) return deny;
            if (string.IsNullOrWhiteSpace(locationCode))
                return Results.BadRequest(new { error = "locationCode is required" });
            if (!DateOnly.TryParse(date, out var d))
                return Results.BadRequest(new { error = "Invalid date format. Expected yyyy-MM-dd." });

            var result = await svc.GetCandidatesAsync(locationCode, d);
            return result == null
                ? Results.NotFound(new { error = "Unknown or inactive WIC location" })
                : Results.Ok(result);
        });
    }
}
