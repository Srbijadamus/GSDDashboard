using GSDDashboard.API.Data.Models;
using System.Text.RegularExpressions;

namespace GSDDashboard.API.Services;

/// <summary>
/// THE single definition of the WIC WorkingShift rule (RTM, 2026-10-14):
///
///   <c>WicShiftEntries.WorkingShift</c> for an on-site row is the INTERSECTION of
///   (a) the agent's own <c>ShiftEntries</c> window (ShiftStart..ShiftEnd) for that
///       employee and date, and
///   (b) the centre's resolved duty window for that date
///       (<see cref="WicHoursResolver.Resolve"/> then <see cref="WicHoursResolver.DutyWindow"/>,
///       so a manual exception day / Rollout day wins and a two-interval centre's break is
///       spanned).
///
/// The agent's <c>ShiftEntries</c> times are NEVER changed because of this rule.
///
/// Before this class every writer had its own copy of the intersection (or none at all):
/// WicShiftService.CreateAssignmentAsync intersected only when the request carried no
/// times, RosterService.SegmentFor re-implemented it, and Shift Plan sync, Assign Agent,
/// Substitute accept, Rollout and VWIC wrote centre hours or request times verbatim. The
/// 2026-10-14 RTM fix found 347 live on-site rows whose WorkingShift left the centre's
/// window, which is exactly the drift this helper removes.
/// </summary>
public static class WicWorkingShift
{
    private static readonly Regex Hhmm = new(@"\d{1,2}:\d{2}", RegexOptions.Compiled);

    /// <summary>
    /// First HH:mm .. last HH:mm of a stored WorkingShift. Tolerant of the two-block shape
    /// ("07:00-12:00 / 12:30-13:30") every centre-hours writer uses, which is a break inside
    /// one spanning duty, not two duties. Null when there are fewer than two times or the
    /// end is not after the start.
    /// </summary>
    public static (TimeOnly Start, TimeOnly End)? Parse(string? workingShift)
    {
        if (string.IsNullOrWhiteSpace(workingShift)) return null;
        var times = Hhmm.Matches(workingShift);
        if (times.Count < 2) return null;
        return TimeOnly.TryParse(times[0].Value, out var s) &&
               TimeOnly.TryParse(times[^1].Value, out var e) && e > s
            ? (s, e) : null;
    }

    /// <summary>Overlap of two half-open windows; null when they do not touch.</summary>
    public static (TimeOnly Start, TimeOnly End)? Intersect(
        (TimeOnly Start, TimeOnly End) a, (TimeOnly Start, TimeOnly End) b)
    {
        var s = a.Start > b.Start ? a.Start : b.Start;
        var e = a.End   < b.End   ? a.End   : b.End;
        return e > s ? (s, e) : null;
    }

    /// <summary>
    /// The centre's duty window for a date — legacy location code included, exception day
    /// preferred. Returns (null, null) when the centre is closed or carries no usable times.
    /// </summary>
    public static (string? Start, string? End) CentreWindow(
        IEnumerable<WicOpeningHour> allHours, WicLocation? location, DateOnly date)
    {
        if (location == null) return (null, null);
        var hours = WicHoursResolver.Resolve(allHours, location.LocationCode, location.LocationCodeLegacy,
            (int)date.DayOfWeek, date);
        return WicHoursResolver.DutyWindow(hours);
    }

    /// <summary>
    /// THE RULE, as a string for <c>WicShiftEntries.WorkingShift</c>.
    ///
    ///   * both windows known  -> their intersection, or null when they do not touch;
    ///   * no centre window    -> the agent's own window (nothing to cut against — the
    ///                            convention RosterService and CreateAssignmentAsync use);
    ///   * no agent window     -> the centre window (a blank agent shift carries nothing
    ///                            to intersect).
    ///
    /// A null result means the two windows do not overlap at all: the caller keeps its own
    /// previous value / fallback and the row is reported for RTM (see the 2026-10-14 point 5).
    /// </summary>
    public static string? Slice(string? agentStart, string? agentEnd, string? centreStart, string? centreEnd)
    {
        var centre = Window(centreStart, centreEnd);
        var agent  = Window(agentStart, agentEnd);
        if (agent == null)  return centre == null ? null : Fmt(centre.Value);
        if (centre == null) return Fmt(agent.Value);
        var ix = Intersect(agent.Value, centre.Value);
        return ix == null ? null : Fmt(ix.Value);
    }

    /// <summary>
    /// READ side for the conflict detector: the window the agent is really committed to at
    /// one centre = the stored WorkingShift CUT TO the centre's window for the date.
    /// Returns (null, null) when the two do not touch (the agent is not on-site during any
    /// open hour, so the row is not a real clash). Falls back to the centre window when the
    /// stored value carries no usable times — the reading every surface used before.
    /// </summary>
    public static (string? Start, string? End) EffectiveWindow(string? workingShift, WicOpeningHour? hours)
    {
        var centre = WicHoursResolver.DutyWindow(hours);
        var stored = Parse(workingShift);
        if (stored == null) return centre;
        var cw = Window(centre.Start, centre.End);
        if (cw == null) return (stored.Value.Start.ToString("HH:mm"), stored.Value.End.ToString("HH:mm"));
        var ix = Intersect(stored.Value, cw.Value);
        return ix == null ? (null, null) : (ix.Value.Start.ToString("HH:mm"), ix.Value.End.ToString("HH:mm"));
    }

    /// <summary>
    /// The value to write for an on-site row when <see cref="Slice"/> finds NO overlap
    /// between the agent's window and the centre's window: the CENTRE's own window, never
    /// the agent's daily shift.
    ///
    /// RTM 2026-10-15 (item 4): on a day with two or more centres each WIC row belongs to
    /// ONE centre and must be cut to THAT centre's working hours. Writing the day's span
    /// into a row whose centre is outside it would make both rows claim the same hours and
    /// destroy the per-centre split (the Demmin Am Hanseufer / Woldeforster case, where the
    /// agent's own 13:00-15:00 window had to stay a 10:00-12:00 row for Am Hanseufer).
    /// Null when the centre carries no usable window — the caller keeps its previous
    /// fallback.
    /// </summary>
    public static string? FallbackWindow(string? centreStart, string? centreEnd) =>
        Window(centreStart, centreEnd) is { } c ? Fmt(c) : null;

    /// <summary>
    /// The day's committed window across ALL of its WIC centres: earliest WIC part start ..
    /// latest WIC part end. Null when the day is not a multi-centre day (fewer than two
    /// usable parts) or nothing can be parsed.
    ///
    /// RTM fact 2026-10-15: an agent can work in two centres on the same day (10-12 centre A,
    /// 13-15 centre B). <c>ShiftEntries</c> is unique per employee+date, so that single daily
    /// row must cover every one of his WIC parts; <c>WicShiftEntries</c> stays one row per
    /// centre.
    /// </summary>
    public static (TimeOnly Start, TimeOnly End)? MultiCentreSpan(IEnumerable<string?> workingShifts)
    {
        var valid = workingShifts
            .Select(Parse)
            .Where(w => w.HasValue)
            .Select(w => w!.Value)
            .ToList();
        if (valid.Count < 2) return null;

        var start = valid.Min(w => w.Start);
        var end   = valid.Max(w => w.End);
        return end > start ? (start, end) : null;
    }

    /// <summary>
    /// Widens a day's own window so that it CONTAINS <paramref name="span"/>. A window that
    /// already covers the span is returned unchanged — the daily shift is only ever widened,
    /// never narrowed.
    /// </summary>
    public static (TimeOnly Start, TimeOnly End) Widen(
        (TimeOnly Start, TimeOnly End)? current, (TimeOnly Start, TimeOnly End) span)
    {
        if (current is not { } c) return span;
        var start = c.Start < span.Start ? c.Start : span.Start;
        var end   = c.End   > span.End   ? c.End   : span.End;
        return (start, end);
    }

    /// <summary>
    /// THE MULTI-CENTRE RULE applied to the daily <c>ShiftEntries</c> row, as strings:
    /// given the row's current ShiftStart/ShiftEnd and every on-site WIC row's WorkingShift
    /// for that date, the window it must carry — or null when nothing has to change.
    ///
    /// Returns null when the day is not multi-centre, when no part parses, or when the
    /// current window already covers the span. This is what stops a single centre's cell
    /// edit (Shift Plan, Assign Agent, + New Shift) from narrowing the daily shift so that
    /// the agent's other centre falls out of it.
    /// </summary>
    public static (string Start, string End)? WidenDaily(
        string? currentStart, string? currentEnd, IEnumerable<string?> partWorkingShifts)
    {
        if (MultiCentreSpan(partWorkingShifts) is not { } span) return null;

        var widened = Widen(Window(currentStart, currentEnd), span);
        var start   = widened.Start.ToString("HH:mm");
        var end     = widened.End.ToString("HH:mm");
        return start == currentStart && end == currentEnd ? null : (start, end);
    }

    private static (TimeOnly Start, TimeOnly End)? Window(string? start, string? end) =>
        TimeOnly.TryParse(start, out var s) && TimeOnly.TryParse(end, out var e) && e > s ? (s, e) : null;

    private static string Fmt((TimeOnly Start, TimeOnly End) w) =>
        $"{w.Start:HH:mm}-{w.End:HH:mm}";
}
