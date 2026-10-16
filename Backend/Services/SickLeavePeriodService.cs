using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

/// <summary>
/// Loads the raw sickness evidence (Krankmeldung rows in <c>SickLeaves</c> plus the manual
/// /shifts SL <c>ShiftEntries</c> cells) for a set of employees and turns it into merged
/// <see cref="SickPeriod"/>s with the shared <see cref="SickLeavePeriodResolver"/> rule.
///
/// This is the ONE place that answers "which sickness period is this agent in, since when,
/// and when is he expected back". Every surface (Sick Leave modal/list, Overview absence
/// feed, Briefing "Absent today") must read its answer from here instead of reading a single
/// row.
/// </summary>
public class SickLeavePeriodService
{
    private readonly GSDContext _db;

    public SickLeavePeriodService(GSDContext db) { _db = db; }

    /// <summary>Resolved periods for one employee plus the working-day rule for that employee.</summary>
    public sealed class PeriodSet
    {
        public required List<SickPeriod> Periods { get; init; }
        public required Func<DateOnly, bool> IsWorkingDay { get; init; }

        public SickPeriod? ActiveOn(DateOnly date) => SickLeavePeriodResolver.ActiveOn(Periods, date);

        /// <summary>First working day after the period's last sick day (yyyy-MM-dd), null for open-ended.</summary>
        public string? ExpectedReturn(SickPeriod period) =>
            SickLeavePeriodResolver.ExpectedReturn(period, IsWorkingDay)?.ToString("yyyy-MM-dd");
    }

    public async Task<Dictionary<string, PeriodSet>> BuildAsync(
        IReadOnlyCollection<string> employeeIds, DateOnly from, DateOnly to)
    {
        var result = new Dictionary<string, PeriodSet>(StringComparer.OrdinalIgnoreCase);
        var ids = employeeIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0) return result;

        // A spell that is still running today may have started months ago, so always look far
        // enough back that the period start is included. Clamped because callers may pass
        // DateOnly.MinValue/MaxValue for an unbounded list request.
        var floor = new DateOnly(2000, 1, 1);
        var ceiling = new DateOnly(2100, 1, 1);
        var loadFrom = from.AddDays(-400);
        if (loadFrom < floor) loadFrom = floor;
        var loadTo = to > ceiling ? ceiling : to;
        if (loadTo < loadFrom) loadTo = loadFrom;

        var kinds = SickLeavePeriodResolver.SickLeaveKinds;
        var records = await _db.SickLeaves
            .Where(s => s.EmployeeId != null && ids.Contains(s.EmployeeId)
                     && s.LeaveType != null && kinds.Contains(s.LeaveType)
                     && s.FirstDay <= loadTo && s.LastDay >= loadFrom)
            .Select(s => new { s.EmployeeId, s.FirstDay, s.LastDay })
            .ToListAsync();

        var slCells = await _db.ShiftEntries
            .Where(e => e.ShiftType == "SL" && ids.Contains(e.EmployeeId)
                     && e.ShiftDate >= loadFrom && e.ShiftDate <= loadTo)
            .Select(e => new { e.EmployeeId, e.ShiftDate })
            .ToListAsync();

        // Every row, not only SL: the OFF / public-holiday markers are what decide whether a
        // gap between two sick days is a working day (which would split the spell).
        var allShifts = await _db.ShiftEntries
            .Where(e => ids.Contains(e.EmployeeId) && e.ShiftDate >= loadFrom && e.ShiftDate <= loadTo)
            .Select(e => new { e.EmployeeId, e.ShiftDate, e.ShiftType, e.Id })
            .ToListAsync();

        var holidays = (await _db.PublicHolidays
            .Where(h => h.HolidayDate >= loadFrom && h.HolidayDate <= loadTo)
            .Select(h => h.HolidayDate)
            .ToListAsync()).ToHashSet();

        var bestTypeByEmp = new Dictionary<string, Dictionary<DateOnly, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in allShifts.GroupBy(e => e.EmployeeId, StringComparer.OrdinalIgnoreCase))
        {
            var byDate = new Dictionary<DateOnly, string>();
            foreach (var day in group.GroupBy(e => e.ShiftDate))
                byDate[day.Key] = day.OrderByDescending(e => e.Id).First().ShiftType;
            bestTypeByEmp[group.Key] = byDate;
        }

        foreach (var id in ids)
        {
            var raw = new List<SickPeriod>();
            foreach (var r in records)
                if (string.Equals(r.EmployeeId, id, StringComparison.OrdinalIgnoreCase))
                    raw.Add(new SickPeriod(r.FirstDay, r.LastDay));
            foreach (var c in slCells)
                if (string.Equals(c.EmployeeId, id, StringComparison.OrdinalIgnoreCase))
                    raw.Add(new SickPeriod(c.ShiftDate, c.ShiftDate));
            if (raw.Count == 0) continue;

            var byDate = bestTypeByEmp.TryGetValue(id, out var map) ? map : new Dictionary<DateOnly, string>();

            bool IsOff(DateOnly d) =>
                SickLeavePeriodResolver.IsWeekend(d)
                || holidays.Contains(d)
                || (byDate.TryGetValue(d, out var t) && SickLeavePeriodResolver.IsNonWorkingShiftType(t));

            result[id] = new PeriodSet
            {
                Periods = SickLeavePeriodResolver.Merge(raw, IsOff),
                IsWorkingDay = d => !IsOff(d),
            };
        }

        return result;
    }
}
