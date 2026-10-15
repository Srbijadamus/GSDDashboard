using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

/// <summary>
/// THE Shift Plan -&gt; WIC sync for a <c>WIC_DUTY</c> row (RTM 2026-10-15, follow-up to the
/// WIC-over-BO rule). A row that the Shift Plan shows as WIC duty MUST have its on-site
/// <c>WicShiftEntries</c> row, or the two coverage readings disagree:
///
///   * <c>/api/wic/cards</c> (and the forecast / the gap detector) read the ShiftEntries row
///     through <see cref="AvailabilityResolver.IsWicDutyAt"/> — they call the day COVERED;
///   * <c>/api/wic/open</c> is SIGNAL 1 ONLY: it counts <c>WicShiftEntries</c> rows with
///     <c>IsOnSite=true</c> — it calls the same day UNCOVERED.
///
/// That mismatch is exactly what the live data showed (Wesel, 9074345, 2026-11-05/12/19/26,
/// RosterBatch 265). The batch's OVERWRITE path removed the day's <c>WicShiftEntries</c>
/// rows and then found the still-tracked, DELETED entity, updated its fields and let
/// <c>SaveChanges</c> delete it — the WIC record was written into a row that was already
/// being removed. This helper is the single implementation of that write:
///
///   * <c>WorkingShift</c> = the agent's window CUT TO the centre's resolved window
///     (<see cref="WicWorkingShift.Slice"/>), or the centre's window when the two do not
///     touch (<see cref="WicWorkingShift.FallbackWindow"/>) — the RTM rule of 2026-10-14,
///     never a second copy of it;
///   * a row THIS unit of work is deleting is REVIVED, never duplicated and never lost
///     (the <c>(EmployeeId, ShiftDate, SupportLocation)</c> key is unique);
///   * an already-correct on-site row is left untouched (idempotent, safe to run on every
///     save);
///   * a CLOSED centre gets no row: there is no WIC duty to attend.
///
/// <see cref="GSDContext"/> calls <see cref="Apply"/> from <c>SaveChanges</c> /
/// <c>SaveChangesAsync</c> for every Added/Modified <c>WIC_DUTY</c> row, so the roster
/// generator, its overwrite path, the imports and every later writer produce the WIC record
/// in the SAME unit of work — the same enforcement point
/// <see cref="WicBackOfficeSync"/> uses for a plan back-office day and
/// <c>GSDContext.LinkWicRowsToShiftTypeChanges</c> for the opposite transition.
///
/// The row's centre is the one its <c>AgentTask</c> names (<see cref="WicLocationMatcher.MatchesAgentTask"/>)
/// — the single definition of "this task field names this centre". A WIC_DUTY row with no
/// centre in <c>AgentTask</c> is left alone: nothing can tell WHICH centre it belongs to, and
/// the readers do not count it as cover anywhere, so inventing one would create a new
/// mismatch instead of removing one.
///
/// Manual rows, leave types (AL/SL/UL/OL/PH/LPH), HALF_AL, OFF/OFF_WEEKEND, TRAINING,
/// RESIGNED, the flipped <c>WORKING</c>+centre encoding and <c>RawValue="BO"</c> plan rows are
/// never touched here: the first group is not a WIC_DUTY row, the flipped encoding keeps the
/// documented "a writer that leaves WIC_DUTY stands the on-site row down" behaviour, and the
/// plan BO day is <see cref="WicBackOfficeSync"/>'s business.
/// </summary>
public static class WicShiftPlanSync
{
    /// <summary>
    /// Ensure the on-site <c>WicShiftEntries</c> row for every WIC_DUTY row in
    /// <paramref name="rows"/>. Returns how many rows were created or updated.
    /// </summary>
    public static int Apply(GSDContext db, IReadOnlyCollection<ShiftEntry> rows)
    {
        var candidates = rows
            .Where(IsWicDutyRow)
            .GroupBy(r => (r.EmployeeId, r.ShiftDate))
            .Select(g => g.First())
            .ToList();
        if (candidates.Count == 0) return 0;

        // Queries + anything already tracked in this unit of work: the rule runs BEFORE the
        // inserts of the same SaveChanges, so a writer that adds the location or the hours in
        // the very same unit of work must still be seen (Local views).
        var locations = UnionWithLocal(
            db.WicLocations.Where(l => l.IsActive).ToList(),
            db.WicLocations.Local, l => l.IsActive);
        var hours = UnionWithLocal(
            db.WicOpeningHours.ToList(),
            db.WicOpeningHours.Local, _ => true);

        var changed = 0;
        foreach (var row in candidates)
        {
            var centre = locations.FirstOrDefault(l => WicLocationMatcher.MatchesAgentTask(row.AgentTask, l));
            if (centre == null) continue;   // no centre named -> nothing may be guessed
            // The central hook FILLS A GAP and keeps the row live; it never re-times a row
            // that already exists. The daily ShiftEntries row of a multi-centre day is a
            // SPAN across the centres, so re-cutting an existing per-centre row with it would
            // destroy that centre's own slice (the 2-open-MAIN Demmin case). A writer that
            // knows the day's decided window for THIS centre re-times through
            // EnsureOnSiteRow below, in its own unit of work.
            if (Ensure(db, row.EmployeeId, row.ShiftDate, centre,
                    row.ShiftStart, row.ShiftEnd, hours, retime: false))
                changed++;
        }
        return changed;
    }

    /// <summary>A <c>WIC_DUTY</c> ShiftEntries row — the type is the authority (RTM 2026-10-15).</summary>
    public static bool IsWicDutyRow(ShiftEntry? row) =>
        row != null && string.Equals(row.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates or re-times ONE on-site <c>WicShiftEntries</c> row for
    /// (employee, date, centre). For a writer that KNOWS the day's decided window for this
    /// centre: the roster generator (its plan segment) and the Shift Plan cell. Returns true
    /// when it created or updated something.
    /// </summary>
    public static bool EnsureOnSiteRow(
        GSDContext db, string employeeId, DateOnly date, WicLocation centre,
        string? agentStart, string? agentEnd, IReadOnlyList<WicOpeningHour> hours,
        bool cutToCentreWindow = true) =>
        Ensure(db, employeeId, date, centre, agentStart, agentEnd, hours, retime: true, cutToCentreWindow);

    private static bool Ensure(
        GSDContext db, string employeeId, DateOnly date, WicLocation centre,
        string? agentStart, string? agentEnd, IReadOnlyList<WicOpeningHour> hours,
        bool retime, bool cutToCentreWindow = true)
    {
        // A closed centre has no WIC duty to attend (same rule WicBackOfficePriority uses).
        if (!WicHoursResolver.IsOpenOn(hours, centre, date)) return false;

        var (centreStart, centreEnd) = WicWorkingShift.CentreWindow(hours, centre, date);
        // The RTM rule of 2026-10-14: the agent's window CUT TO the centre's window, never
        // the centre's hours standing in for the agent's shift. A caller whose window IS
        // already the day's decided slice for this centre (the roster generator's repeated
        // multi-centre decision) asks for it verbatim.
        var computed = cutToCentreWindow
            ? WicWorkingShift.Slice(agentStart, agentEnd, centreStart, centreEnd)
              ?? WicWorkingShift.FallbackWindow(centreStart, centreEnd)
            : WicWorkingShift.Slice(agentStart, agentEnd, null, null);

        var dayRows = DayRows(db, employeeId, date);
        var target = dayRows
            .FirstOrDefault(w => WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, centre));

        if (target == null)
        {
            if (string.IsNullOrWhiteSpace(computed)) return false;
            db.WicShiftEntries.Add(new WicShiftEntry
            {
                EmployeeId      = employeeId,
                ShiftDate       = date,
                DayOfWeek       = date.DayOfWeek.ToString(),
                SupportLocation = centre.DisplayName,
                LocationCode    = centre.LocationCode,
                WorkingShift    = computed,
                IsOnSite        = true,
                IsGSDDay        = false,
                IsOffDay        = false,
                Task            = "WIC",
            });
            return true;
        }

        // A row the same unit of work is deleting (the roster overwrite path) is REVIVED
        // instead of lost: the (EmployeeId, ShiftDate, SupportLocation) key is unique, so
        // this really is the one row the day may have for this centre.
        var entry = db.Entry(target);
        if (entry.State == EntityState.Deleted) entry.State = EntityState.Modified;

        // Once the row exists its own window is the authority. Only a caller that knows the
        // day's decided window for THIS centre (retime: the roster generator's plan segment,
        // the Shift Plan cell) or a row carrying no window at all takes the computed value.
        // The daily ShiftEntries row of a multi-centre day is a SPAN across the centres, and
        // even on a single-centre day it can be a repeated DECISION that deliberately differs
        // from the centre's window (RosterMultiMainSameDayTests) — so the hook must never
        // re-time an existing row.
        var workingShift = retime || string.IsNullOrWhiteSpace(target.WorkingShift)
            ? (computed ?? target.WorkingShift)
            : target.WorkingShift;

        var needsUpdate = !target.IsOnSite
            || !string.Equals(target.WorkingShift, workingShift, StringComparison.Ordinal)
            || !string.Equals(target.LocationCode, centre.LocationCode, StringComparison.Ordinal)
            || target.IsGSDDay
            || target.IsOffDay
            || !string.Equals(target.Task, "WIC", StringComparison.Ordinal)
            || !string.Equals(target.SupportLocation, centre.DisplayName, StringComparison.Ordinal);
        if (!needsUpdate) return false;

        target.SupportLocation = centre.DisplayName;
        target.LocationCode    = centre.LocationCode;
        target.WorkingShift    = workingShift;
        target.IsOnSite        = true;
        target.IsGSDDay        = false;
        target.IsOffDay        = false;
        target.Task            = "WIC";
        return true;
    }

    /// <summary>
    /// Every <c>WicShiftEntries</c> row of that employee+date: the database rows (which
    /// include the ones this unit of work is deleting — a query returns the tracked instance,
    /// including a <c>Deleted</c> one) plus the <c>Added</c> rows it cannot see yet.
    /// </summary>
    private static List<WicShiftEntry> DayRows(GSDContext db, string employeeId, DateOnly date)
    {
        var rows = db.WicShiftEntries
            .Where(w => w.EmployeeId == employeeId && w.ShiftDate == date)
            .ToList();
        foreach (var tracked in db.ChangeTracker.Entries<WicShiftEntry>()
                     .Where(e => e.State == EntityState.Added)
                     .Select(e => e.Entity))
        {
            if (string.Equals(tracked.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase)
                && tracked.ShiftDate == date
                && !rows.Contains(tracked))
                rows.Add(tracked);
        }
        return rows;
    }

    /// <summary>Rows the query returned plus the tracked entities it cannot see yet.</summary>
    private static List<T> UnionWithLocal<T>(
        List<T> fromQuery, IEnumerable<T> local, Func<T, bool> predicate) where T : class
    {
        foreach (var entity in local)
            if (predicate(entity) && !fromQuery.Contains(entity))
                fromQuery.Add(entity);
        return fromQuery;
    }
}
