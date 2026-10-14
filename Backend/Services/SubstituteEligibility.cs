using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

// ── Who may be offered as a substitute / available agent ──────────────────────
// ONE definition for every suggestion surface, so they can never disagree:
//   * WicCoverageGapService       — the Overview banner's "Available: …" floaters,
//   * WicAssignCandidatesService  — the Assign picker on a coverage-gap row,
//   * SubstitutionService         — BriefingService's "Best substitute" and Find Substitute,
//   * OverviewService             — the per-day topSubstitute field of /api/overview/wic-status.
//
// Live defect (Hannover, 2026-11-02..06): employee 9130648 carries
// ShiftEntries.ShiftType = RESIGNED from 2026-11-02, but Employees.IsActive is still 1.
// WicCoverageGapService only removed its AbsenceTypes set (AL/HALF_AL/SL/UL/OL/PH/LPH/
// OFF/CD) from the "available floaters" list — RESIGNED is deliberately not in that set
// (a leaver is not "absent") — so the resigned floater was published as "Available:" on
// five consecutive days. SubstitutionService read the resignation only when the RESIGNED
// row happened to be the row selected for that exact day, so the same leavers (9130648,
// 9087657, 9124687) still came back as day-off call-in candidates on 2026-11-03..06.
//
// The rule here is the historical one, not the same-day one: a RESIGNED row dated on or
// BEFORE the day being planned means the agent has left by that day, even when no row
// exists for the day itself. `Employees.IsActive` is the other half of the same rule and
// is applied by every surface that loads employees.
public static class SubstituteEligibility
{
    /// <summary>Shift type carrying the day an agent left the company.</summary>
    public const string Resigned = "RESIGNED";

    /// <summary>
    /// Earliest RESIGNED ShiftEntries date per employee, from every RESIGNED row up to the
    /// last date a surface will ask about. One dictionary then answers
    /// "has left on/before date" for every date in the window with no query per day.
    /// </summary>
    public static Dictionary<string, DateOnly> EarliestResignedByEmployee(IEnumerable<ShiftEntry> rows) =>
        rows.Where(r => string.Equals(r.ShiftType, Resigned, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(r => r.ShiftDate), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// true when a RESIGNED ShiftEntries row exists for this employee on or before
    /// <paramref name="date"/> — i.e. the agent had already left by that day.
    /// </summary>
    public static bool ResignedOnOrBefore(
        string? employeeId, DateOnly date, IReadOnlyDictionary<string, DateOnly> earliestResigned) =>
        !string.IsNullOrEmpty(employeeId) &&
        earliestResigned.TryGetValue(employeeId, out var from) && from <= date;

    /// <summary>
    /// true when the employee has left by <paramref name="date"/>: the Employees row is
    /// inactive, or a RESIGNED ShiftEntries row is dated on or before it. A null employee
    /// (name that matches no record) is not treated as resigned here; the caller keeps its
    /// own rules for that case.
    /// </summary>
    public static bool HasLeft(
        Employee? employee, DateOnly date, IReadOnlyDictionary<string, DateOnly> earliestResigned) =>
        employee is { IsActive: false } ||
        ResignedOnOrBefore(employee?.EmployeeId, date, earliestResigned);
}
