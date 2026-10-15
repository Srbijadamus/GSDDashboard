using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

/// <summary>
/// THE single definition of the WIC-over-BO priority rule (owner decision, 2026-10-15).
///
///   BO = Back Office. <b>WIC has priority over BO.</b> An agent who holds an ACTIVE WIC
///   assignment (MAIN / BACKUP / REGIONAL) and whose plan says BO goes to WIC — UNLESS RTM
///   set BO on that day BY HAND, in which case BO stands. The centre's opening hours always
///   apply: WIC presence = the agent's shift intersected with the centre's working window
///   (<see cref="WicWorkingShift"/>).
///
/// This REPLACES the previous blanket rule "a BO row is never WIC duty in any reader"
/// (commit 84e71e4). That rule was right about ONE case — a BO day a human had authored —
/// and wrong about the other: an automatically generated BO day for an agent who is
/// permanently assigned to a centre is exactly the day the WIC priority rule exists for.
///
/// What counts as a BO <i>day</i>
/// -----------------------------
/// A row is a BO day when it is not WIC duty by type and carries the BO marker:
///   * <c>RawValue = "BO"</c> — how the roster generator writes a back-office day
///     (<c>RosterService.ApplyGenerated</c>: ShiftType=WORKING, RawValue="BO",
///     AgentTask=null); or
///   * <c>ShiftType = "BO"</c> — the literal shape the manual Shift Plan API accepts
///     (<see cref="WicAttendanceLink.BackOfficeShiftType"/>).
///
/// A <c>WIC_DUTY</c> row is WIC duty whatever its RawValue still says — the generator and
/// earlier imports left <c>RawValue="BO"</c> on a handful of real WIC_DUTY rows, and the
/// type is the authority. Those rows are never re-classified as BO.
///
/// Manual vs plan
/// --------------
///   * <see cref="IsManualBackOffice"/> — a BO day with <c>ManualChangeBy</c> set. The human
///     authored this row, so BO stands and it is never read as WIC.
///   * <see cref="IsPlanBackOffice"/> — a BO day no human has touched. If the agent has an
///     active WIC assignment with an OPEN centre on that date, the day is WIC duty at that
///     centre.
///
/// Which centre
/// ------------
/// A plan BO row carries no centre name (the generator sets AgentTask=null). The centre is
/// therefore resolved from the agent's ACTIVE assignments the same way
/// <c>WicShiftService.CreateAssignmentAsync</c> and <c>RosterService.GenerateLocationCoverageAsync</c>
/// order their candidates: MAIN first (in assignment order), then BACKUP, then REGIONAL —
/// and only a centre that is OPEN on the date counts, because a closed centre has no WIC
/// duty to attend.
/// </summary>
public static class WicBackOfficePriority
{
    /// <summary>
    /// The provenance marker the 2026-10-15 rule writes onto every ShiftEntries row it
    /// reclassifies (<c>SourceModule</c>). Deliberately does NOT stamp ManualChangeBy: this
    /// is an automatic rule, not a human decision.
    /// </summary>
    public const string RuleMarker = "RTM_RULE 2026-10-15 WIC>BO";

    private static readonly string[] AssignmentRank = { "MAIN", "BACKUP", "REGIONAL" };

    /// <summary>A back-office DAY: WORKING (or the literal "BO") carrying the BO marker.</summary>
    public static bool IsBackOfficeDay(ShiftEntry? sh)
    {
        if (sh == null) return false;
        var isWorking = string.Equals(sh.ShiftType, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase);
        var isLiteralBo = string.Equals(sh.ShiftType, WicAttendanceLink.BackOfficeShiftType, StringComparison.OrdinalIgnoreCase);
        if (!isWorking && !isLiteralBo) return false;   // leave/training/OFF are never BO days
        return isLiteralBo || AvailabilityResolver.IsBackOfficeRawValue(sh.RawValue);
    }

    /// <summary>A BO day a human authored — BO stands, never WIC.</summary>
    public static bool IsManualBackOffice(ShiftEntry? sh) => IsBackOfficeDay(sh) && sh!.IsManualChange;

    /// <summary>A BO day from the plan (no manual RTM edit) — WIC priority applies.</summary>
    public static bool IsPlanBackOffice(ShiftEntry? sh) => IsBackOfficeDay(sh) && !sh!.IsManualChange;

    /// <summary>
    /// Employee id -&gt; the ordered ACTIVE WIC centres of that employee. Built once per read
    /// and passed to <see cref="AvailabilityResolver.IsWicDutyAt(ShiftEntry?, WicLocation, WicAssignmentIndex, IEnumerable{WicOpeningHour})"/>
    /// so every reader resolves a plan BO row to the same centre.
    /// </summary>
    public sealed class WicAssignmentIndex
    {
        private readonly Dictionary<string, IReadOnlyList<(string Type, WicLocation Location)>> _byEmployee =
            new(StringComparer.OrdinalIgnoreCase);

        internal void Add(string employeeId, IReadOnlyList<(string Type, WicLocation Location)> locations) =>
            _byEmployee[employeeId] = locations;

        internal IReadOnlyList<(string Type, WicLocation Location)> Pairs(string? employeeId) =>
            employeeId != null && _byEmployee.TryGetValue(employeeId, out var list)
                ? list
                : Array.Empty<(string, WicLocation)>();

        public IReadOnlyList<WicLocation> For(string? employeeId) =>
            Pairs(employeeId).Select(p => p.Location).ToList();

        public bool HasAssignment(string? employeeId) => Pairs(employeeId).Count > 0;

        public static readonly WicAssignmentIndex Empty = new();
    }

    /// <summary>
    /// Build the index from the active assignment rows, the active locations and the
    /// employees the names resolve to. Assignments are matched by
    /// <c>EmployeeName == Employees.FullName</c> (WicAgentAssignments has no EmployeeId
    /// column) and by <see cref="WicLocationMatcher.MatchesAssignmentCode"/> (new tilde code
    /// or legacy bridge code).
    /// </summary>
    public static WicAssignmentIndex BuildIndex(
        IEnumerable<WicAgentAssignment> assignments,
        IEnumerable<WicLocation> locations,
        IEnumerable<Employee> employees)
    {
        var index = new WicAssignmentIndex();

        var activeLocations = locations.Where(l => l.IsActive).ToList();
        var byName = assignments
            .Where(a => a.IsActive && AssignmentRank.Contains(a.AssignmentType, StringComparer.OrdinalIgnoreCase))
            .GroupBy(a => a.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var emp in employees)
        {
            if (emp?.EmployeeId == null || string.IsNullOrWhiteSpace(emp.FullName)) continue;
            if (!byName.TryGetValue(emp.FullName, out var list)) continue;

            var ordered = list
                .OrderBy(a => Array.FindIndex(AssignmentRank,
                    r => string.Equals(r, a.AssignmentType, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(a => a.Id)
                .Select(a => (
                    Type: AssignmentRank.FirstOrDefault(r =>
                        string.Equals(r, a.AssignmentType, StringComparison.OrdinalIgnoreCase)) ?? a.AssignmentType.ToUpperInvariant(),
                    Location: activeLocations.FirstOrDefault(l => WicLocationMatcher.MatchesAssignmentCode(a.LocationCode, l))))
                .Where(x => x.Location != null)
                .Select(x => (Type: x.Type, Location: x.Location!))
                .DistinctBy(x => x.Location.LocationCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count > 0) index.Add(emp.EmployeeId, ordered);
        }

        return index;
    }

    /// <summary>
    /// The centre a plan BO day counts at, resolved tier by tier in MAIN -&gt; BACKUP -&gt;
    /// REGIONAL order: the ONE open centre of the first tier that has any open centre that
    /// day. Two or more open centres in a tier is AMBIGUOUS and resolves to null — the same
    /// "never guess between two open MAIN centres" rule the roster generator applies
    /// (<c>RosterService.BuildPlanAsync</c>), so the day stays a genuine back-office day.
    /// Null also when the agent has no open assigned centre at all: a closed centre has no
    /// WIC duty to attend.
    /// </summary>
    public static WicLocation? PriorityCentre(
        WicAssignmentIndex? index, string? employeeId, DateOnly date, IEnumerable<WicOpeningHour>? allHours)
    {
        if (index == null || allHours == null) return null;
        var hours = allHours as IReadOnlyList<WicOpeningHour> ?? allHours.ToList();
        foreach (var tier in AssignmentRank)
        {
            var open = index.Pairs(employeeId)
                .Where(p => string.Equals(p.Type, tier, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Location)
                .Where(l => WicHoursResolver.IsOpenOn(hours, l, date))
                .ToList();
            if (open.Count > 1) return null;   // ambiguous — the generator's no-guess rule
            if (open.Count == 1) return open[0];
        }
        return null;
    }

    /// <summary>
    /// The shared reading: a plan BO day counts as WIC duty at <paramref name="loc"/> exactly
    /// when <paramref name="loc"/> is <see cref="PriorityCentre"/> for that agent and date.
    /// Callers that only have the two-argument
    /// <see cref="AvailabilityResolver.IsWicDutyAt(ShiftEntry?, WicLocation)"/> fall back to
    /// the row's own AgentTask (the flipped-ShiftType encoding) and therefore never see a
    /// plan BO row — this overload is the one every coverage reader must use.
    /// </summary>
    public static bool IsPlanBackOfficeWicAt(
        ShiftEntry? sh, WicLocation loc, WicAssignmentIndex? index, IEnumerable<WicOpeningHour>? allHours)
    {
        if (!IsPlanBackOffice(sh)) return false;
        var centre = PriorityCentre(index, sh!.EmployeeId, sh.ShiftDate, allHours);
        return centre != null &&
               string.Equals(centre.LocationCode, loc.LocationCode, StringComparison.OrdinalIgnoreCase);
    }
}
