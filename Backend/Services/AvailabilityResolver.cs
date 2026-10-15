using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public enum AbsenceStatus
{
    WORKING,
    HALF_AL,
    SL,
    AL,
    OFF,
    OFF_WEEKEND,
    PH,
    TRAINING,
    RESIGNED,
    UNKNOWN
}

public class AvailabilityResolver
{
    private readonly GSDContext _db;

    // Canonical full-absence set (sourced from BackupService, OverviewService, BriefingService,
    // ForecastService, WicShiftService -- all five define this identical set).
    // SubstitutionService intentionally adds TRAINING for its own scheduling logic; that
    // local addition stays in SubstitutionService and is not reflected here.
    public static readonly HashSet<string> FullAbsenceTypes =
        new(StringComparer.OrdinalIgnoreCase) { "SL", "AL", "UL", "OL", "PH", "LPH", "RESIGNED" };

    // Blocking absence set used by the WIC assignment endpoint (WicShiftService.AssignAgent)
    // and by the coverage card calculation (WicCardsService). Superset of FullAbsenceTypes:
    // also treats OFF and OFF_WEEKEND as absent. Single source of truth — do not
    // re-declare this list elsewhere.
    //
    // HALF_AL is deliberately NOT in this set (RTM 2026-10-15, requirement 1). A half-AL day
    // is half annual leave AND half WORK — the agent is available for the working part of
    // the day, so a WIC duty may be placed on him for that part. Full AL/SL/UL/OL/PH/LPH and
    // OFF/OFF_WEEKEND/RESIGNED still block: on those days there is no working part at all.
    public static readonly HashSet<string> BlockingAbsenceTypes =
        new(StringComparer.OrdinalIgnoreCase) { "AL", "SL", "UL", "PH", "LPH", "OL", "OFF", "OFF_WEEKEND", "RESIGNED" };

    // The leave types the Overview "Absent today" people list / KPI reports.
    //
    // Deliberately NOT FullAbsenceTypes. Coverage keeps RESIGNED there because a resigned
    // agent contributes 0 cover, but a person who has left the company is not "absent
    // today" and listing them as such is simply wrong (2 such rows were live on
    // 2026-10-13). HALF_AL is the opposite case: half a day away is an absence the
    // FullAbsenceTypes set does not contain.
    public static readonly HashSet<string> AbsenceFeedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ShiftTypes.SickLeave, ShiftTypes.AnnualLeave, ShiftTypes.UnpaidLeave,
            ShiftTypes.OtherLeave, ShiftTypes.PublicHol, ShiftTypes.LocalPH, ShiftTypes.HalfAL,
        };

    // Reusable as a List<string> for EF Core IN-clause translation.
    private static readonly List<string> FullAbsenceTypesList = FullAbsenceTypes.ToList();

    public AvailabilityResolver(GSDContext db) => _db = db;

    // Returns the WIC coverage contribution for a scheduled agent.
    // 1.0 = doing WIC duty; 0.5 = HALF_AL; 0.0 = absent, doing non-WIC work, or no
    // ShiftEntry at all. Same rule as RosterService.IsAvailable: unknown is not
    // present — a missing ShiftEntry can't rule out an absence that was never
    // imported, so it must not be silently counted as coverage.
    // All callers use this instead of local if-else chains to keep coverage logic in one place.
    public static double GetWicContribution(bool isSick, ShiftEntry? sh)
    {
        if (isSick) return 0.0;
        if (sh == null) return 0.0; // no ShiftEntry → unknown, not verified present
        if (FullAbsenceTypes.Contains(sh.ShiftType)) return 0.0;
        if (string.Equals(sh.ShiftType, ShiftTypes.HalfAL, StringComparison.OrdinalIgnoreCase)) return 0.5;
        if (string.Equals(sh.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase))
            // WIC_DUTY + GSD/Backlog AgentTask = agent is doing non-WIC work; introduced 2026-09-03 (223 hist. rows, 0 current)
            return (string.Equals(sh.AgentTask, "GSD",     StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sh.AgentTask, "Backlog", StringComparison.OrdinalIgnoreCase))
                ? 0.0 : 1.0;
        return 0.0; // WORKING or other non-WIC type → agent is doing other work, not WIC duty
    }

    // Shift types that mean "not on WIC duty, whatever AgentTask still says".
    // Deliberately broader than FullAbsenceTypes: HALF_AL/OFF/OFF_WEEKEND/CD/TRAINING
    // are also not WIC cover. This set is what protects the gap endpoint from the
    // stale-AgentTask artefact — a leave import overwrites ShiftType but leaves the
    // previous AgentTask behind, so the DB holds e.g. 14 AL rows still tagged
    // "Hamburg". Reading those as coverage would hide exactly the gaps the endpoint
    // exists to find (it is how the Hamburg AL block was caught in the first place).
    private static readonly HashSet<string> NotOnWicDutyTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ShiftTypes.SickLeave, ShiftTypes.AnnualLeave, ShiftTypes.UnpaidLeave, ShiftTypes.OtherLeave,
            ShiftTypes.PublicHol, ShiftTypes.LocalPH, ShiftTypes.Resigned, ShiftTypes.HalfAL,
            ShiftTypes.Off, ShiftTypes.OffWeekend, ShiftTypes.CompDay, ShiftTypes.CompOff,
            ShiftTypes.Training,
        };

    /// <summary>
    /// The RawValue a BACK-OFFICE day carries. A plan BO day is written by the roster
    /// generator as a WORKING row with RawValue="BO" and NO AgentTask; the manual Shift Plan
    /// API additionally accepts the literal <c>ShiftType="BO"</c>.
    ///
    /// Whether a BO row is WIC duty is NOT decided here — the owner rule (2026-10-15) is
    /// "WIC has priority over BO, unless RTM set BO by hand", which needs the row's manual
    /// marker and the agent's active WIC assignment. That decision lives in
    /// <see cref="WicBackOfficePriority"/>; this helper only answers "does the raw value say
    /// BO", the same way every reader has always asked it.
    /// </summary>
    public static bool IsBackOfficeRawValue(string? rawValue) =>
        string.Equals(rawValue?.Trim(), "BO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Single definition of "this ShiftEntries row means the agent is on WIC duty at
    /// <paramref name="loc"/> that day". Every reader of the ShiftEntries cover signal
    /// calls this instead of testing <c>ShiftType == WIC_DUTY</c> itself.
    ///
    /// Why it is not just a ShiftType test: <c>AgentTask</c> carries a WIC centre's
    /// DisplayName *only* on the WIC_DUTY encoding — on a plain WORKING row the column
    /// holds one of the seven task badges ("WIC", "Voice", ...) or "GSD", never a centre
    /// name. A row whose AgentTask names a centre is therefore a WIC-duty row even when
    /// its ShiftType reads WORKING, and reading ShiftType alone turns one wrong field
    /// into a phantom "UNSTAFFED" day that no RTM can explain from the UI (the task cell
    /// shows the centre while the day is reported uncovered).
    ///
    /// Such rows exist: RosterBatch 155/156/170 (CreatedByKid=SYSTEM_WICFIX, 2026-09-30
    /// 06:09, overwrite mode) left rows with RawValue=null + AgentTask=&lt;centre&gt; but
    /// ShiftType=WORKING, after which the Program.cs startup cleanup zeroed their
    /// IsWicDuty flag. Independently corroborated by data that cannot be explained any
    /// other way: Merlin Voss 2026-10-01/02 carries exactly this shape AND a
    /// WicShiftEntries IsOnSite=1 row at the same centre (Helmstedt).
    ///
    /// Absence and away types are never reinterpreted — see NotOnWicDutyTypes.
    ///
    /// A BO row is WIC duty only per the owner rule (2026-10-15): WIC has priority over BO,
    /// so a PLAN BO day (no manual RTM edit) for an agent with an active WIC assignment is
    /// WIC duty at that centre — see
    /// <see cref="IsWicDutyAt(ShiftEntry?, WicLocation, WicBackOfficePriority.WicAssignmentIndex, IEnumerable{WicOpeningHour})"/>,
    /// which is the overload every coverage reader uses. A BO day RTM authored stays BO and
    /// is never WIC (see <see cref="WicBackOfficePriority.IsManualBackOffice"/>).
    /// </summary>
    public static bool IsWicDutyAt(ShiftEntry? sh, WicLocation loc)
    {
        if (sh == null) return false;                                    // unknown is not present
        if (WicBackOfficePriority.IsManualBackOffice(sh)) return false;  // RTM set BO by hand
        // A PLAN BO row's centre is not in the row (the generator writes AgentTask=null); it
        // comes from the agent's assignment. Without that context there is nothing to match,
        // so this overload cannot call it WIC duty — use the assignment-aware overload.
        if (WicBackOfficePriority.IsPlanBackOffice(sh)) return false;
        if (!WicLocationMatcher.MatchesAgentTask(sh.AgentTask, loc)) return false;

        var type = sh.ShiftType ?? "";
        if (string.Equals(type, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase)) return true;
        if (NotOnWicDutyTypes.Contains(type)) return false;
        // WORKING with a centre in AgentTask = the WIC_DUTY encoding, ShiftType flipped.
        // Any other/unrecognised type stays false: unknown is not present.
        return string.Equals(type, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The location-aware reading every coverage reader uses. Identical to
    /// <see cref="IsWicDutyAt(ShiftEntry?, WicLocation)"/> except for a PLAN BO row, which
    /// carries no centre name (the generator writes AgentTask=null): its centre is resolved
    /// from the agent's active WIC assignment (MAIN, then BACKUP, then REGIONAL — only a
    /// centre OPEN on the date counts). Owner rule 2026-10-15: BO counts as non-WIC ONLY
    /// when RTM set it by hand; otherwise WIC has priority.
    /// </summary>
    public static bool IsWicDutyAt(
        ShiftEntry? sh, WicLocation loc,
        WicBackOfficePriority.WicAssignmentIndex? assignments,
        IEnumerable<WicOpeningHour>? openingHours)
    {
        if (sh == null) return false;
        if (WicBackOfficePriority.IsManualBackOffice(sh)) return false;  // RTM set BO by hand
        if (WicBackOfficePriority.IsPlanBackOffice(sh))
            return WicBackOfficePriority.IsPlanBackOfficeWicAt(sh, loc, assignments, openingHours);
        return IsWicDutyAt(sh, loc);
    }

    /// <summary>
    /// Location-aware WIC contribution: <see cref="GetWicContribution"/> plus the
    /// IsWicDutyAt reading of the row. Callers that know which centre they are counting
    /// for should use this; the location-agnostic overload cannot tell a WIC-duty row
    /// with a flipped ShiftType from an agent genuinely doing other work, and scores
    /// both 0.0.
    /// </summary>
    public static double GetWicContributionAt(bool isSick, ShiftEntry? sh, WicLocation loc)
    {
        if (isSick) return 0.0;
        if (IsWicDutyAt(sh, loc)) return 1.0;
        return GetWicContribution(isSick, sh);
    }

    /// <summary>
    /// Assignment-aware <see cref="GetWicContributionAt"/>: a PLAN BO row counts as WIC duty
    /// at the agent's priority centre (owner rule 2026-10-15). Every coverage reader passes
    /// the index + hours it already loaded.
    /// </summary>
    public static double GetWicContributionAt(
        bool isSick, ShiftEntry? sh, WicLocation loc,
        WicBackOfficePriority.WicAssignmentIndex? assignments,
        IEnumerable<WicOpeningHour>? openingHours)
    {
        if (isSick) return 0.0;
        if (IsWicDutyAt(sh, loc, assignments, openingHours)) return 1.0;
        return GetWicContribution(isSick, sh);
    }

    /// <summary>
    /// SIGNAL-1 contribution (RTM 2026-10-15, requirement 1): the agent HAS an on-site
    /// <c>WicShiftEntries</c> row at this centre for this date, so the question is not
    /// "how available is he in general" but "is he staffing this centre".
    ///
    /// Identical to <see cref="GetWicContribution"/> except for a HALF_AL day. There the
    /// ShiftEntries row's ShiftStart..ShiftEnd is the agent's WORKING part (the other half
    /// is annual leave) and the on-site row's WorkingShift is exactly that part cut to the
    /// centre's window (<see cref="WicWorkingShift"/>). The agent is therefore on WIC duty
    /// at this centre for every hour he is contracted to work, and the centre he is booked
    /// into must not be graded with the generic half-day availability score — that score
    /// would declare his own centre uncovered and offer a substitute for a day that is
    /// fully staffed. HALF_AL without an on-site row still scores 0.5 (the generic
    /// half-day availability).
    /// </summary>
    public static double GetOnSiteWicContribution(bool isSick, ShiftEntry? sh)
    {
        if (isSick) return 0.0;
        if (sh != null && string.Equals(sh.ShiftType, ShiftTypes.HalfAL, StringComparison.OrdinalIgnoreCase))
            return 1.0;
        // WIC OVER BO (owner, 2026-10-15): an automatic back-office day whose agent holds an
        // active WIC assignment IS this centre's WIC duty. Without this the on-site row the
        // sync writes would be scored 0.0 (a WORKING row) and the centre would still read
        // uncovered. A BO day RTM authored is excluded — see IsManualBackOffice.
        if (WicBackOfficePriority.IsPlanBackOffice(sh)) return 1.0;
        return GetWicContribution(isSick, sh);
    }

    /// <summary>
    /// Location-aware <see cref="GetOnSiteWicContribution"/>. Keeps the flipped-ShiftType
    /// reading of <see cref="IsWicDutyAt"/> (an on-site agent whose ShiftType was flipped
    /// to WORKING still counts) and adds the HALF_AL working-part rule.
    /// </summary>
    public static double GetOnSiteWicContribution(bool isSick, ShiftEntry? sh, WicLocation loc)
    {
        if (isSick) return 0.0;
        if (IsWicDutyAt(sh, loc)) return 1.0;
        return GetOnSiteWicContribution(isSick, sh);
    }

    /// <summary>
    /// Assignment-aware <see cref="GetOnSiteWicContribution(bool, ShiftEntry?, WicLocation)"/>:
    /// a PLAN BO day whose agent holds an active WIC assignment is this centre's WIC duty
    /// (owner rule 2026-10-15), scored with the same signal-1 semantics as any other on-site
    /// agent.
    /// </summary>
    public static double GetOnSiteWicContribution(
        bool isSick, ShiftEntry? sh, WicLocation loc,
        WicBackOfficePriority.WicAssignmentIndex? assignments,
        IEnumerable<WicOpeningHour>? openingHours)
    {
        if (isSick) return 0.0;
        if (IsWicDutyAt(sh, loc, assignments, openingHours)) return 1.0;
        return GetOnSiteWicContribution(isSick, sh);
    }

    // Returns the absence status for a single employee on a given date.
    // SickLeave records take priority over ShiftEntries.
    public async Task<AbsenceStatus> GetStatusAsync(string employeeId, DateOnly date)
    {
        var hasSickLeave = await _db.SickLeaves.AnyAsync(s =>
            s.EmployeeId == employeeId && s.FirstDay <= date && s.LastDay >= date);
        if (hasSickLeave) return AbsenceStatus.SL;

        var entry = await _db.ShiftEntries.FirstOrDefaultAsync(s =>
            s.EmployeeId == employeeId && s.ShiftDate == date);
        if (entry == null) return AbsenceStatus.UNKNOWN;

        return (entry.ShiftType ?? "").ToUpperInvariant() switch
        {
            "SL"          => AbsenceStatus.SL,
            "AL"          => AbsenceStatus.AL,
            "UL"          => AbsenceStatus.AL,
            "OL"          => AbsenceStatus.AL,
            "HALF_AL"     => AbsenceStatus.HALF_AL,
            "OFF"         => AbsenceStatus.OFF,
            "OFF_WEEKEND" => AbsenceStatus.OFF_WEEKEND,
            "PH"          => AbsenceStatus.PH,
            "LPH"         => AbsenceStatus.PH,
            "TRAINING"    => AbsenceStatus.TRAINING,
            "RESIGNED"    => AbsenceStatus.RESIGNED,
            _             => AbsenceStatus.WORKING
        };
    }

    // Returns the subset of the supplied employee IDs that are absent on the given date.
    // An employee is absent if they have an active SickLeave record OR a ShiftEntry
    // whose ShiftType is in FullAbsenceTypes.
    public async Task<HashSet<string>> GetAbsentIdsAsync(IEnumerable<string> employeeIds, DateOnly date)
    {
        var ids = employeeIds.ToList();
        if (ids.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sickIds = await _db.SickLeaves
            .Where(s => s.EmployeeId != null
                     && ids.Contains(s.EmployeeId)
                     && s.FirstDay <= date
                     && s.LastDay >= date)
            .Select(s => s.EmployeeId!)
            .ToListAsync();

        var shiftAbsentIds = await _db.ShiftEntries
            .Where(s => s.EmployeeId != null
                     && ids.Contains(s.EmployeeId)
                     && s.ShiftDate == date
                     && FullAbsenceTypesList.Contains(s.ShiftType))
            .Select(s => s.EmployeeId!)
            .ToListAsync();

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in sickIds)        result.Add(id);
        foreach (var id in shiftAbsentIds) result.Add(id);
        return result;
    }
}
