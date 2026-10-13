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
    // also treats HALF_AL, OFF and OFF_WEEKEND as absent. Single source of truth — do not
    // re-declare this list elsewhere.
    public static readonly HashSet<string> BlockingAbsenceTypes =
        new(StringComparer.OrdinalIgnoreCase) { "AL", "HALF_AL", "SL", "UL", "PH", "LPH", "OL", "OFF", "OFF_WEEKEND", "RESIGNED" };

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
    /// </summary>
    public static bool IsWicDutyAt(ShiftEntry? sh, WicLocation loc)
    {
        if (sh == null) return false;                                    // unknown is not present
        if (!WicLocationMatcher.MatchesAgentTask(sh.AgentTask, loc)) return false;

        var type = sh.ShiftType ?? "";
        if (string.Equals(type, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase)) return true;
        if (NotOnWicDutyTypes.Contains(type)) return false;
        // WORKING with a centre in AgentTask = the WIC_DUTY encoding, ShiftType flipped.
        // Any other/unrecognised type stays false: unknown is not present.
        return string.Equals(type, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase);
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
