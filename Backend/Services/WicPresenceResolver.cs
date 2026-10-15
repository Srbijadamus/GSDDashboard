using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;

namespace GSDDashboard.API.Services;

/// <summary>
/// Where a location's coverage evidence comes from. Exposed so a surface can say
/// *why* a location is covered instead of silently disagreeing with another surface.
/// </summary>
public enum WicCoverageEvidence
{
    /// <summary>Nobody is contributing coverage — a real gap if the centre is open.</summary>
    NONE,

    /// <summary>At least one contributing agent has an on-site WicShiftEntries row.</summary>
    ON_SITE,

    /// <summary>
    /// Coverage exists, but ONLY from ShiftEntries WIC duty — no contributing agent has an
    /// on-site WicShiftEntries row. The centre is staffed; the on-site record is missing.
    /// This is the SYSTEM_WICFIX import defect (521 date+location rows as of 2026-09-30:
    /// RosterBatch writes created the ShiftEntries side and never wrote the WicShiftEntries
    /// side). A record-keeping problem, not a staffing gap — and it must never be presented
    /// as one, which is exactly what BriefingService and WicCardsService used to do.
    /// </summary>
    ROSTER_ONLY,
}

/// <summary>One agent's contribution to a location's coverage on one date.</summary>
public record WicPresentAgent(
    string EmployeeId,
    double Contribution,
    bool HasOnSiteRow,
    ShiftEntry? Shift,
    WicShiftEntry? OnSiteRow)
{
    /// <summary>
    /// Rostered for WIC duty here, but with no on-site row to confirm it — the agents a
    /// card should badge "staffed, check-in not recorded".
    ///
    /// A MANUAL row never raises it: an ad-hoc deployment or hand-edited duty has no
    /// paired on-site row by design, so flagging it would turn a legitimate decision into
    /// a data-quality complaint. Manual rows are surfaced with a neutral "Manual" label
    /// instead (ShiftEntry.IsManualChange).
    /// </summary>
    public bool CheckInNotRecorded =>
        Contribution > 0 && !HasOnSiteRow && !(Shift?.IsManualChange ?? false);

    /// <summary>True when the duty behind this contribution was set by hand.</summary>
    public bool IsManualChange => Shift?.IsManualChange ?? false;
}

/// <summary>The resolved coverage picture for one location on one date.</summary>
public record WicPresence(
    List<WicPresentAgent> Agents,
    double PresentDouble,
    int EffectiveCoverage,
    int OnSiteCount,
    int CheckInNotRecordedCount,
    WicCoverageEvidence Evidence,
    /// <summary>How many contributing agents are here by a manual decision.</summary>
    int ManualCount = 0)
{
    public static readonly WicPresence Empty =
        new(new List<WicPresentAgent>(), 0, 0, 0, 0, WicCoverageEvidence.NONE);
}

/// <summary>
/// The single definition of "who is covering this WIC location on this date, and how do we
/// know". Both cover signals are unioned here, once:
///   1. an on-site <c>WicShiftEntries</c> row (IsOnSite=1), matched by SupportLocation; and
///   2. a <c>ShiftEntries</c> row that <see cref="AvailabilityResolver.IsWicDutyAt"/> reads
///      as WIC duty at this centre.
/// Agents found by both are counted once, by employee.
///
/// Why this exists: the union used to live inline in ForecastService only. BriefingService
/// (Overview "Gaps &amp; Recommendations") and WicCardsService (WIC Locations page) built
/// presence from signal 1 ALONE, so on 2026-10-01 they reported 19 open centres as
/// 0/1 UNCOVERED — complete with a "Find Substitute" button — while the banner, the
/// Coverage Risk strip and the KPI cards all said the same 19 were covered. Every one of
/// those 19 had a WIC_DUTY ShiftEntries row and no on-site row (RosterBatches 153-192,
/// CreatedByKid=SYSTEM_WICFIX). Five surfaces, five implementations, two of them missing a
/// signal. Any new surface reads coverage through this type or it will drift the same way.
///
/// Pure and allocation-light by design: callers bulk-load their own rows for whatever
/// window they need and pass them in, so this is callable per location per date inside an
/// existing loop without extra queries.
/// </summary>
public static class WicPresenceResolver
{
    /// <summary>
    /// Pre-filter for signal 2: the only <c>ShiftEntries</c> rows that could name a centre.
    /// A task badge (or "GSD") never can, so those are dropped up front — the expensive
    /// per-location match then runs over a much smaller set. Passing an unfiltered list
    /// still produces the same answer, just slower.
    /// </summary>
    public static List<ShiftEntry> CandidateDutyRows(IEnumerable<ShiftEntry> shiftEntries) =>
        shiftEntries
            .Where(s => !string.IsNullOrEmpty(s.AgentTask) && !AgentTasks.IsNonLocationValue(s.AgentTask))
            .ToList();

    /// <param name="onSiteRows">
    /// WicShiftEntries rows for any location/date; filtered here to this location, this date
    /// and IsOnSite=1. Callers may pre-filter for speed but must not pre-filter by location.
    /// </param>
    /// <param name="shiftByEmpDate">
    /// Best ShiftEntry per (employee, date) — <see cref="ShiftDuplicateResolver.BestShiftEntry"/>.
    /// Used to score on-site agents; a missing entry scores 0.0 (unknown is not present).
    /// </param>
    /// <param name="candidateDutyRows">Signal 2 candidates, see <see cref="CandidateDutyRows"/>.</param>
    /// <param name="isSick">
    /// SickLeaves cross-check by employee id, separate from ShiftEntries. Pass
    /// <c>_ =&gt; false</c> when the caller has no SickLeaves data loaded — ShiftSyncService
    /// normally also writes ShiftType=SL, so absences are still caught by the ShiftEntry.
    /// </param>
    public static WicPresence Resolve(
        WicLocation loc,
        DateOnly date,
        IEnumerable<WicShiftEntry> onSiteRows,
        IReadOnlyDictionary<(string, DateOnly), ShiftEntry> shiftByEmpDate,
        IEnumerable<ShiftEntry> candidateDutyRows,
        Func<string, bool> isSick)
    {
        var agents = new List<WicPresentAgent>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Signal 1: on-site rows ──────────────────────────────────────────────
        foreach (var w in onSiteRows)
        {
            if (w.ShiftDate != date || !w.IsOnSite) continue;
            if (TestAccounts.IsExcluded(w.EmployeeId)) continue;
            if (!WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc)) continue;
            if (!seen.Add(w.EmployeeId)) continue;

            shiftByEmpDate.TryGetValue((w.EmployeeId, date), out var sh);
            // Location-aware on purpose: an on-site agent whose ShiftEntries row is a
            // WIC-duty row with a flipped ShiftType still counts (see IsWicDutyAt), and a
            // HALF_AL day with an on-site row counts as the working part he is there for
            // (GetOnSiteWicContribution — RTM 2026-10-15, requirement 1).
            var contribution = AvailabilityResolver.GetOnSiteWicContribution(isSick(w.EmployeeId), sh, loc);
            agents.Add(new WicPresentAgent(w.EmployeeId, contribution, HasOnSiteRow: true, sh, w));
        }

        // ── Signal 2: ShiftEntries WIC duty with no on-site row ─────────────────
        foreach (var s in candidateDutyRows)
        {
            if (s.ShiftDate != date) continue;
            if (TestAccounts.IsExcluded(s.EmployeeId)) continue;
            if (!AvailabilityResolver.IsWicDutyAt(s, loc)) continue;
            if (!seen.Add(s.EmployeeId)) continue;   // already counted via signal 1

            var contribution = AvailabilityResolver.GetWicContributionAt(isSick(s.EmployeeId), s, loc);
            agents.Add(new WicPresentAgent(s.EmployeeId, contribution, HasOnSiteRow: false, s, null));
        }

        var presentDouble = agents.Sum(a => a.Contribution);
        var contributing = agents.Where(a => a.Contribution > 0).ToList();
        var onSiteCount = contributing.Count(a => a.HasOnSiteRow);
        var notRecorded = contributing.Count(a => a.CheckInNotRecorded);

        // ROSTER_ONLY only when NOTHING is confirmed on site. A location where 2 of 3
        // required agents are confirmed reports ON_SITE; CheckInNotRecordedCount still
        // tells a card how many check-ins are missing, so the badge is independent of this.
        var evidence = contributing.Count == 0 ? WicCoverageEvidence.NONE
                     : onSiteCount > 0         ? WicCoverageEvidence.ON_SITE
                                               : WicCoverageEvidence.ROSTER_ONLY;

        return new WicPresence(
            agents, presentDouble, (int)Math.Floor(presentDouble),
            onSiteCount, notRecorded, evidence,
            contributing.Count(a => a.IsManualChange));
    }
}
