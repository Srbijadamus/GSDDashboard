namespace GSDDashboard.API.Services;

/// <summary>
/// ONE continuous sickness spell, expressed as a calendar day range.
///
/// A spell is not "one Krankmeldung" and not "one SL ShiftEntries row": RTMs record an
/// ongoing sickness day by day with the manual /shifts SL cell (a ShiftEntries row per
/// day, often including the WE rows) while the Krankmeldung behind it may end earlier or
/// be split. Those rows are the same spell, so they must be merged before anything is
/// displayed.
/// </summary>
public sealed record SickPeriod(DateOnly FirstDay, DateOnly LastDay)
{
    /// <summary>An open-ended record (LastDay in 2099) is still ongoing.</summary>
    public bool OpenEnded => LastDay.Year >= 2099;

    public int CalendarDays => LastDay.DayNumber - FirstDay.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= FirstDay && date <= LastDay;
}

/// <summary>
/// THE shared sickness-period rule for every Pulse surface that shows "sick since",
/// "expected return", "days so far" or the duration of an absence: the "Currently Sick
/// Agents" modal, the Sick Leave active list, the absence list and the Overview/Briefing
/// "Absent today" feed.
///
/// WHY: before 2026-10-16 each surface answered the question itself. The modal used the
/// single SL ShiftEntries row (so an ongoing spell looked like "sick since today, expected
/// return tomorrow, 1d"), the Briefing used only a Krankmeldung covering the day (and fell
/// back to "1d" when the Krankmeldung had already ended while the shift cells continued).
/// The history was in the database all along — the readers simply did not join it.
///
/// RULES (owner rule, 2026-10-16):
///  - Consecutive sick days are ONE period when the days between them are not working days:
///    weekends, public holidays and OFF days without work in between do not break a spell.
///  - "Sick since" = first day of the period.
///  - "Expected return" = first working day AFTER the last recorded sick day (skips weekend,
///    public holidays and OFF days). Open-ended = no expected return.
///  - "Days so far" = reference day - first day + 1 (calendar days).
///  - Duration in the absence list = period end - period start + 1.
/// </summary>
public static class SickLeavePeriodResolver
{
    /// <summary>
    /// A gap longer than this is never bridged even if every day in it is a non-working day.
    /// Keeps a stray row months away from merging into an unrelated spell.
    /// </summary>
    public const int MaxGapScanDays = 31;

    /// <summary>How far past the last sick day the first working day is looked for.</summary>
    public const int MaxReturnLookaheadDays = 60;

    /// <summary>LeaveType values in SickLeaves that are sickness (SL is the legacy spelling of Self).</summary>
    public static readonly List<string> SickLeaveKinds = new() { "SL", "Self", "Child" };

    /// <summary>ShiftEntry types that mean "the agent was not expected to work that day".</summary>
    public static readonly HashSet<string> NonWorkingShiftTypes = new(StringComparer.OrdinalIgnoreCase)
        { "OFF", "OFF_WEEKEND", "PH", "LPH", "CD", "CO" };

    public static bool IsWeekend(DateOnly day) =>
        day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static bool IsNonWorkingShiftType(string? shiftType) =>
        shiftType != null && NonWorkingShiftTypes.Contains(shiftType);

    /// <summary>
    /// Merge raw sick intervals into continuous periods. Two intervals join when they
    /// overlap/touch, or when every calendar day between them is a non-working day
    /// (<paramref name="isNonWorkingGapDay"/>: weekend, public holiday, OFF day).
    /// </summary>
    public static List<SickPeriod> Merge(IEnumerable<SickPeriod> source, Func<DateOnly, bool> isNonWorkingGapDay)
    {
        var ordered = source
            .Where(p => p.LastDay >= p.FirstDay)
            .OrderBy(p => p.FirstDay).ThenBy(p => p.LastDay)
            .ToList();

        var merged = new List<SickPeriod>();
        foreach (var interval in ordered)
        {
            if (merged.Count == 0) { merged.Add(interval); continue; }
            var current = merged[^1];

            bool join;
            if (interval.FirstDay.DayNumber <= current.LastDay.DayNumber + 1)
            {
                join = true; // overlap or directly adjacent
            }
            else
            {
                var gapFrom = current.LastDay.AddDays(1);
                var gapTo = interval.FirstDay.AddDays(-1);
                join = gapTo.DayNumber - gapFrom.DayNumber + 1 <= MaxGapScanDays
                    && AllNonWorking(gapFrom, gapTo, isNonWorkingGapDay);
            }

            if (join)
                merged[^1] = current with { LastDay = interval.LastDay > current.LastDay ? interval.LastDay : current.LastDay };
            else
                merged.Add(interval);
        }
        return merged;
    }

    private static bool AllNonWorking(DateOnly from, DateOnly to, Func<DateOnly, bool> isNonWorkingGapDay)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
            if (!isNonWorkingGapDay(d)) return false;
        return true;
    }

    /// <summary>The period covering <paramref name="date"/>, or null when the agent is not sick then.</summary>
    public static SickPeriod? ActiveOn(IEnumerable<SickPeriod> periods, DateOnly date) =>
        periods.Where(p => p.Contains(date))
               .OrderByDescending(p => p.FirstDay)
               .FirstOrDefault();

    /// <summary>
    /// First working day strictly after the period's last recorded sick day.
    /// Null when the period is open-ended or no working day is found within the lookahead.
    /// </summary>
    public static DateOnly? ExpectedReturn(SickPeriod period, Func<DateOnly, bool> isWorkingDay)
    {
        if (period.OpenEnded) return null;
        var day = period.LastDay;
        for (var i = 0; i < MaxReturnLookaheadDays; i++)
        {
            day = day.AddDays(1);
            if (isWorkingDay(day)) return day;
        }
        return null;
    }
}
