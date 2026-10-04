using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

/// <summary>
/// THE single definition of "is this centre open on this date, and when".
/// Every surface resolves hours through here — ForecastService, BriefingService,
/// WicCoverageGapService, WicCardsService, RosterService, WicShiftService and (via
/// /api/wic/forecast + /api/wic/cards) the Locations page, the Overview KPIs and WIC
/// Attendance. Because of that, a manual exception day recorded once is open EVERYWHERE.
/// </summary>
public static class WicHoursResolver
{
    /// <summary>
    /// From a flat list of all WicOpeningHour rows, return the single effective row
    /// for a given location + day-of-week + date (picks the latest EffectiveFrom <= date).
    /// </summary>
    public static WicOpeningHour? Resolve(
        IEnumerable<WicOpeningHour> all,
        string locationCode,
        int dayOfWeek,
        DateOnly date)
    {
        return Resolve(all, locationCode, null, dayOfWeek, date);
    }

    /// <summary>
    /// The single working window one ShiftEntries row must carry for a WIC duty at this
    /// centre: the first interval's open through the LAST interval's close. A centre with
    /// a second interval (OpenTime2/CloseTime2) is open either side of a break, and the
    /// RTM rule (2026-10-01) is that the gap is only a break — duty spans it and the break
    /// is never a coverage hole.
    ///
    /// One spanning row is not a preference, it is what the schema allows: ShiftEntries
    /// carries a single ShiftStart/ShiftEnd pair and has three unique indexes that each cap
    /// it at one row per agent-day (UQ_ShiftEntries_EmpDate on (EmployeeId, ShiftDate),
    /// plus ...EmpDateType and ...EmpDateSheet), so a second row for the same agent and
    /// date cannot be inserted at all. The codebase already uses this convention for the
    /// mirror-image read — WicShiftService.SplitWorkingShift collapses a two-block
    /// WorkingShift string to first-start..last-end.
    ///
    /// The break cannot become a hole downstream because CoverageCalculator measures
    /// required time as the sum of the opening intervals only (CalcOpenMinutes) and credits
    /// an agent against each interval separately (CalcAgentCoverage / CoveredByUnion) — the
    /// gap between them belongs to neither, so a spanning agent scores 100%.
    ///
    /// Returns (null, null) when the centre is closed or carries no usable times, so the
    /// caller keeps its own fallback.
    /// </summary>
    public static (string? Start, string? End) DutyWindow(WicOpeningHour? h)
    {
        if (h == null || h.IsClosed) return (null, null);
        var start = FirstNonBlank(h.OpenTime, h.OpenTime2);
        var end   = FirstNonBlank(h.CloseTime2, h.CloseTime);
        return (start == null || end == null) ? (null, null) : (start, end);
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }

    /// <summary>
    /// Overload that also checks the legacy location code.
    /// </summary>
    public static WicOpeningHour? Resolve(
        IEnumerable<WicOpeningHour> all,
        string locationCode,
        string? locationCodeLegacy,
        int dayOfWeek,
        DateOnly date)
    {
        bool ThisLocation(WicOpeningHour h) =>
            h.LocationCode == locationCode ||
            (locationCodeLegacy != null && h.LocationCode == locationCodeLegacy);

        // MANUAL EXCEPTION DAY wins over the weekday pattern. RTM opening (or closing) a
        // centre for one date is a legitimate decision, so the row for that exact date is
        // the answer and the recurring pattern is ignored. DayOfWeek is deliberately NOT
        // matched on an exception row: the point of an exception is that the centre is
        // open on a day its pattern says it is shut.
        var exception = all
            .Where(h => ThisLocation(h) && h.ExceptionDate == date)
            .OrderByDescending(h => h.ManualChangeAt ?? DateTime.MinValue)
            .FirstOrDefault();
        if (exception != null) return exception;

        return all
            .Where(h =>
                ThisLocation(h) &&
                h.ExceptionDate == null &&            // exception rows never act as pattern
                h.DayOfWeek == dayOfWeek &&
                (h.EffectiveFrom == null || h.EffectiveFrom <= date))
            .OrderByDescending(h => h.EffectiveFrom ?? DateOnly.MinValue)
            .FirstOrDefault();
    }
}
