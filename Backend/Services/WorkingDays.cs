using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

/// <summary>
/// Shared working-day counting: Mon–Fri minus the public holidays that apply to a
/// country + Bundesland. Single definition used both by the AL work-day calculation
/// (VacationService.ComputeWorkDaysAsync) and by the AL Balance SL-days calculation
/// (ALBalanceService), so the two can never disagree about "how many working days".
/// </summary>
public static class WorkingDays
{
    /// <summary>Number of Mon–Fri days in [from, to] that are not public holidays
    /// applicable to (country, bundesland). Returns 0 for an empty/negative range.</summary>
    public static int Count(IEnumerable<PublicHoliday> holidays, string country, string? bundesland, DateOnly from, DateOnly to)
    {
        var holidayDates = holidays
            .Where(h => HolidayMatcher.Applies(h, country, bundesland))
            .Select(h => h.HolidayDate)
            .ToHashSet();

        int count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
            if (holidayDates.Contains(d)) continue;
            count++;
        }
        return count;
    }

    /// <summary>Number of DISTINCT Mon–Fri days (minus applicable public holidays) across
    /// a set of possibly-overlapping periods. The same day is counted once, so overlapping
    /// sick-leave spells never double-count.</summary>
    public static int DistinctCount(IEnumerable<(DateOnly From, DateOnly To)> periods, IEnumerable<PublicHoliday> holidays, string country, string? bundesland)
    {
        var holidayDates = holidays
            .Where(h => HolidayMatcher.Applies(h, country, bundesland))
            .Select(h => h.HolidayDate)
            .ToHashSet();

        var days = new HashSet<DateOnly>();
        foreach (var (from, to) in periods)
        {
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
                if (holidayDates.Contains(d)) continue;
                days.Add(d);
            }
        }
        return days.Count;
    }
}
