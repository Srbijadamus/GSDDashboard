using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

/// <summary>
/// THE Shift Plan -&gt; WIC sync for the WIC-over-BO priority rule (owner, 2026-10-15).
///
/// When a ShiftEntries row is an AUTOMATIC back-office day (<see cref="WicBackOfficePriority.IsPlanBackOffice"/>)
/// for an employee who holds an active WIC assignment, this creates/keeps the on-site
/// <c>WicShiftEntries</c> row for the agent's priority centre, with
/// <c>WorkingShift = the agent's shift intersected with the centre's working window</c>
/// — the single rule in <see cref="WicWorkingShift"/>. A row RTM authored
/// (<see cref="WicBackOfficePriority.IsManualBackOffice"/>) is never touched: BO stands.
///
/// Called from <see cref="GSDContext.SaveChanges()"/> / <c>SaveChangesAsync</c> for every
/// Added/Modified plan BO row in the unit of work, so the roster generator, the imports and
/// any later writer all produce the same WIC record in the SAME save as the ShiftEntries
/// change — the same enforcement point <c>GSDContext.LinkWicRowsToShiftTypeChanges</c> uses
/// for the opposite transition.
///
/// Manual rows, leave types (AL/SL/UL/OL/PH/LPH), HALF_AL, OFF/OFF_WEEKEND, training and
/// every row carrying <c>ManualChangeBy</c> are untouched: the only rows this method reads
/// are plan back-office rows.
/// </summary>
public static class WicBackOfficeSync
{
    /// <summary>
    /// Ensure the on-site WIC row for every plan back-office row in <paramref name="rows"/>.
    /// Synchronous on purpose: it runs inside <c>GSDContext.SaveChanges()</c>.
    /// Returns how many WIC rows were created or updated.
    /// </summary>
    public static int Apply(GSDContext db, IReadOnlyCollection<ShiftEntry> rows)
    {
        var candidates = rows
            .Where(WicBackOfficePriority.IsPlanBackOffice)
            .GroupBy(r => (r.EmployeeId, r.ShiftDate))
            .Select(g => g.First())
            .ToList();
        if (candidates.Count == 0) return 0;

        var employeeIds = candidates.Select(c => c.EmployeeId).Distinct().ToList();
        // Queries + anything already tracked in this unit of work: the rule runs BEFORE the
        // inserts of the same SaveChanges, so a writer that adds the employee/centre/hours in
        // the very same unit of work must still be seen (Local views), exactly like the
        // WicShiftEntries lookup below.
        var employees = UnionWithLocal(
            db.Employees.Where(e => employeeIds.Contains(e.EmployeeId)).ToList(),
            db.Employees.Local, e => employeeIds.Contains(e.EmployeeId));
        var names = employees.Where(e => e.FullName != null).Select(e => e.FullName!).Distinct().ToList();
        if (names.Count == 0) return 0;

        var assignments = UnionWithLocal(
            db.WicAgentAssignments.Where(a => a.IsActive && names.Contains(a.EmployeeName)).ToList(),
            db.WicAgentAssignments.Local, a => a.IsActive && names.Contains(a.EmployeeName));
        var locations = UnionWithLocal(
            db.WicLocations.Where(l => l.IsActive).ToList(),
            db.WicLocations.Local, l => l.IsActive);
        var hours = UnionWithLocal(
            db.WicOpeningHours.ToList(),
            db.WicOpeningHours.Local, _ => true);
        var index = WicBackOfficePriority.BuildIndex(assignments, locations, employees);

        // Existing rows for these employee+date pairs: the database plus anything already
        // tracked in this unit of work (the caller may have added — or be about to DELETE —
        // a row itself, e.g. the roster generator's overwrite path).
        var dates = candidates.Select(c => c.ShiftDate).Distinct().ToList();
        var existing = db.WicShiftEntries
            .Where(w => employeeIds.Contains(w.EmployeeId) && dates.Contains(w.ShiftDate))
            .ToList();
        foreach (var tracked in db.ChangeTracker.Entries<WicShiftEntry>()
                     .Where(e => e.State == EntityState.Added)
                     .Select(e => e.Entity))
        {
            if (!existing.Contains(tracked)) existing.Add(tracked);
        }

        var changed = 0;
        foreach (var row in candidates)
        {
            var centre = WicBackOfficePriority.PriorityCentre(index, row.EmployeeId, row.ShiftDate, hours);
            if (centre == null) continue; // no open assigned centre -> the day stays BO

            var (centreStart, centreEnd) = WicWorkingShift.CentreWindow(hours, centre, row.ShiftDate);
            var workingShift = WicWorkingShift.Slice(row.ShiftStart, row.ShiftEnd, centreStart, centreEnd)
                               ?? WicWorkingShift.FallbackWindow(centreStart, centreEnd);
            if (string.IsNullOrWhiteSpace(workingShift)) continue;

            var target = existing.FirstOrDefault(w =>
                string.Equals(w.EmployeeId, row.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
                w.ShiftDate == row.ShiftDate &&
                WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, centre));

            if (target != null && target.IsOnSite &&
                string.Equals(target.WorkingShift, workingShift, StringComparison.Ordinal))
                continue;

            if (target == null)
            {
                var created = new WicShiftEntry
                {
                    EmployeeId      = row.EmployeeId,
                    ShiftDate       = row.ShiftDate,
                    DayOfWeek       = row.ShiftDate.DayOfWeek.ToString(),
                    SupportLocation = centre.DisplayName,
                    LocationCode    = centre.LocationCode,
                    WorkingShift    = workingShift,
                    IsOnSite        = true,
                    IsGSDDay        = false,
                    IsOffDay        = false,
                    Task            = "WIC",
                };
                db.WicShiftEntries.Add(created);
                existing.Add(created);
                changed++;
                continue;
            }

            // A row the same unit of work is deleting (roster overwrite) is revived instead
            // of duplicated: the (EmployeeId, ShiftDate, SupportLocation) key is unique.
            var targetEntry = db.Entry(target);
            if (targetEntry.State == EntityState.Deleted) targetEntry.State = EntityState.Modified;

            target.SupportLocation = centre.DisplayName;
            target.LocationCode    = centre.LocationCode;
            target.WorkingShift    = workingShift;
            target.IsOnSite        = true;
            target.IsGSDDay        = false;
            target.IsOffDay        = false;
            target.Task            = "WIC";
            changed++;
        }

        return changed;
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
