using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

/// <summary>
/// THE single link between the Shift Plan (<c>ShiftEntries</c>) and WIC Attendance
/// (<c>WicShiftEntries</c>). RTM 2026-10-15, requirement 3.
///
///   When a day's <c>ShiftEntries</c> row stops being WIC duty — for ANY other type
///   (WORKING, a BO/back-office day, AL, SL, OFF, ...) — the day's on-site
///   <c>WicShiftEntries</c> rows are stood down in the SAME unit of work, whatever writer
///   made the change.
///
/// Before this the rule existed only inside
/// <see cref="GSDDashboard.API.Modules.Shifts.ShiftService.SyncWicDutyFromShiftPlanAsync"/>,
/// which runs for a HAND Shift Plan cell edit only. Every other writer — Bulk RTM
/// (AL/OFF/CD via /api/shifts/assign/manual, SL via /api/sickleave, AL via /api/vacations),
/// the roster generator, rollout, substitute accept, leave sync/revert, the sickness and
/// vacation reverts, the future-vacation reconciliation, training, and any writer added
/// later — changed <c>ShiftEntries</c> and left the on-site WIC row behind, so WIC
/// Attendance kept showing an agent the Shift Plan had already moved off WIC duty.
///
/// The enforcement point is <see cref="GSDContext.SaveChanges()"/> /
/// <see cref="GSDContext.SaveChangesAsync(bool, CancellationToken)"/>: every EF Core writer
/// goes through it, so no writer can be forgotten. Writers that already manage the row
/// themselves (Shift Plan sync, move-to-GSD, roster overwrite) do exactly the same
/// stand-down, so the hook is idempotent for them.
///
/// "Stand down" means <c>IsOnSite = false</c>, the established mechanism every WIC reader
/// uses (WicPresenceResolver signal 1 reads only <c>IsOnSite=1</c>): the historical row and
/// its WorkingShift survive for the schedule/report, they just stop counting as coverage.
/// Deleting rows is a separate, deliberate data operation — never something a shift-type
/// edit does as a side effect.
///
/// The SECOND half of the hook — clearing a stale centre name from <c>AgentTask</c> — is
/// deliberately much narrower: only a MANUAL RTM edit (Shift Plan cell / Bulk RTM paste)
/// that takes the row to WORKING or BO may do it
/// (<see cref="ClearsCentreOnManualRtmTypeEdit"/>; declared by
/// <c>GSDContext.RecordManualRtmShiftEdit</c>). Every automatic writer stands the WIC row
/// down but keeps the centre name, because that name is the trace of a temporary move and
/// the handle back to the agent's home centre.
/// </summary>
public static class WicAttendanceLink
{
    /// <summary>
    /// True when this is the transition the rule is about: the day WAS WIC duty and is now
    /// something else. A null/unchanged type, or a move INTO WIC_DUTY, is not a transition.
    /// </summary>
    public static bool LeavesWicDuty(string? before, string? after) =>
        string.Equals(before, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(after, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A back-office day stored as a literal ShiftType. In <c>ShiftEntries</c> a BO day is
    /// normally <c>WORKING</c> + <c>RawValue="BO"</c> (see <see cref="AvailabilityResolver.IsBackOfficeRawValue"/>);
    /// this constant only accepts the (equally BO) literal form defensively.
    /// </summary>
    public const string BackOfficeShiftType = "BO";

    /// <summary>
    /// True when a MANUAL RTM type edit (Shift Plan cell / Bulk RTM paste) moved the row from
    /// WIC_DUTY to WORKING or BO. That is the ONLY transition allowed to clear the centre
    /// name from AgentTask: the human has explicitly said "this agent is no longer on WIC duty
    /// here", so leaving the name behind would keep him listed as cover.
    ///
    /// Deliberately NOT true for WIC_DUTY → AL/SL/OFF/... — a leave, a sickness or a day off
    /// is a decision about the DAY, not about the centre — and, above all, not true for any
    /// AUTOMATIC writer. The centre name is the trace of a TEMPORARY move (another WIC centre,
    /// or BO/GSD) and the handle by which the agent returns to his home centre; automation
    /// must never erase it. WIC_DUTY centre A → WIC_DUTY centre B is not a departure from WIC
    /// duty at all, so <see cref="LeavesWicDuty"/> is already false for it and nothing clears.
    /// </summary>
    public static bool ClearsCentreOnManualRtmTypeEdit(string? before, string? after) =>
        LeavesWicDuty(before, after) &&
        (string.Equals(after, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(after, BackOfficeShiftType, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when this <c>ShiftEntries</c> row has just LEFT WIC duty and still carries a
    /// WIC centre's name in <c>AgentTask</c>, so the name must be cleared.
    ///
    /// The centre name is the SECOND cover signal's only handle
    /// (<see cref="AvailabilityResolver.IsWicDutyAt"/> matches <c>AgentTask</c> against a
    /// centre): a WORKING row whose AgentTask names a centre used to read as WIC duty with a
    /// flipped ShiftType, which is why an agent taken off WIC duty kept showing up in the WIC
    /// cards (9129428, Pfaffenhofen, 2026-11-16). Once the type has moved on, the name is
    /// stale data and removing it is the only way every reader stops seeing the phantom duty.
    ///
    /// Only a value that actually names a WIC centre is cleared. A task badge ("GSD",
    /// "Voice", ...) is the cell's own legitimate content and is never touched, and a row
    /// that has no AgentTask is left as it is. Callers pass the ACTIVE centres, which is the
    /// same set (and the same truncated-prefix matcher) the readers resolve against.
    /// </summary>
    public static bool ClearsWicCentreAgentTask(ShiftEntry row, IEnumerable<WicLocation> activeCentres)
    {
        if (string.IsNullOrWhiteSpace(row.AgentTask)) return false;
        var task = row.AgentTask.Trim();
        if (AgentTasks.IsNonLocationValue(task)) return false;
        foreach (var centre in activeCentres)
        {
            // Same match the readers use: exact DisplayName/code, or the 20-char-truncated
            // prefix AgentTask stores for long names. A short task badge can never match.
            if (WicLocationMatcher.MatchesAgentTask(task, centre)) return true;
        }
        return false;
    }

    /// <summary>
    /// Stands down every currently on-site WIC row of one employee on one date, optionally
    /// sparing the row of one centre (the caller is about to upsert that centre — the
    /// Shift-Plan centre A → centre B move: A is stood down, B is written by the caller).
    /// Returns how many rows were changed. A row that is already off-site is left alone.
    /// </summary>
    public static async Task<int> StandDownAsync(
        GSDContext db, string employeeId, DateOnly date, string? keepLocation = null)
    {
        if (string.IsNullOrWhiteSpace(employeeId)) return 0;

        var rows = await db.WicShiftEntries
            .Where(w => w.EmployeeId == employeeId && w.ShiftDate == date && w.IsOnSite)
            .ToListAsync();

        var changed = 0;
        foreach (var w in rows)
        {
            if (keepLocation != null &&
                string.Equals(w.SupportLocation, keepLocation, StringComparison.OrdinalIgnoreCase))
                continue;
            w.IsOnSite = false;
            changed++;
        }
        return changed;
    }
}
