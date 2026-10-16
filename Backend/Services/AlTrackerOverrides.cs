using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

/// <summary>One AL-tracker-ignored day, surfaced as a warning so the AL owner can fix the tracker.</summary>
public record AlTrackerConflict(
    string Date,
    string EmployeeId,
    int AlTrackerEntryId,
    string TrackerFirstDay,
    string TrackerLastDay,
    string ShiftType,
    string? AgentTask,
    string? ShiftStart,
    string? ShiftEnd,
    string? ManualChangeBy,
    string? ManualChangeAt);

/// <summary>
/// OWNER DECISION (2026-10-16): <b>the Shift Plan wins over the AL Tracker.</b>
///
/// The reported defect: RTM removes an AL in the Shift Plan by hand and puts a duty on
/// the day (e.g. WIC_DUTY at a centre), but the AL Tracker (<c>Vacations</c>, the
/// "Annual Leave" tab) still carries the approved leave. Every reader then sees the
/// leftover <c>Vacations</c> row and shows the agent as ABSENT (AL) in WIC cards,
/// kiosk check-in status, /api/wic/open, Overview and the forecast — while the Shift
/// Plan cell says he is on duty.
///
/// The rule implemented here, in ONE place so every reader agrees:
///
///   When the day's <c>ShiftEntries</c> row carries a MANUAL RTM edit
///   (<c>ManualChangeBy</c> set) to a NON-ABSENCE type (WORKING, WIC_DUTY, BO, CD,
///   TRAINING, ...), the AL Tracker entry for that employee+date is IGNORED.
///   Everything else is unchanged:
///     * a tracker entry with no manual edit still means absent (as before);
///     * a manual edit TO an absence (AL/SL/UL/OL/PH/LPH/RESIGNED or HALF_AL) is a
///       decision about the day, so the tracker is honoured — this helper never
///       reclassifies an absence the human entered;
///     * HALF_AL is NOT in the override set — its half-day logic stays exactly as it
///       is (the human-authored HALF_AL tracker/ShiftEntry behaviour is untouched).
///
/// The AL Tracker itself is never changed, and nothing is ever deleted. The ignored
/// day is instead surfaced as a <see cref="AlTrackerConflict"/> warning (date, the
/// employee id and the id of the tracker entry) so the person who maintains the AL
/// tracker can correct it.
/// </summary>
public static class AlTrackerOverrides
{
    /// <summary>
    /// The manual type is a NON-absence decision: it overrides the tracker. HALF_AL is
    /// deliberately excluded (see the class remarks) — a half-AL day keeps its existing
    /// semantics.
    /// </summary>
    public static bool IsNonAbsenceManualType(string? shiftType)
    {
        if (string.IsNullOrWhiteSpace(shiftType)) return false;
        if (AvailabilityResolver.FullAbsenceTypes.Contains(shiftType)) return false;
        if (string.Equals(shiftType, ShiftTypes.HalfAL, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// True when THIS <c>ShiftEntries</c> row is a manual RTM edit to a non-absence type
    /// and therefore suppresses the day's AL-tracker (<c>Vacations</c>) entry.
    /// </summary>
    public static bool IsManualNonAbsenceDuty(ShiftEntry? row) =>
        row?.IsManualChange == true && IsNonAbsenceManualType(row.ShiftType);

    /// <summary>
    /// The tracker row is suppressed by the day's manual duty. Kept as a named predicate so
    /// readers can filter a pre-loaded <c>Vacations</c> list without re-deriving the rule.
    /// </summary>
    public static bool IgnoresTrackerEntry(ShiftEntry? dayRow) => IsManualNonAbsenceDuty(dayRow);

    /// <summary>
    /// The one resolver every reader uses. Given the day's <c>Vacations</c> rows and the
    /// day's best <c>ShiftEntries</c> row per employee, returns the tracker entries to ignore
    /// and the conflicts to warn about. Pure — no database access — so a caller that already
    /// bulk-loaded its window pays nothing extra.
    /// </summary>
    public static (HashSet<int> IgnoreIds, List<AlTrackerConflict> Conflicts) Resolve(
        DateOnly date,
        IEnumerable<Vacation> trackerEntries,
        IReadOnlyDictionary<string, ShiftEntry?>? dayRowByEmployee)
    {
        var ignore = new HashSet<int>();
        var conflicts = new List<AlTrackerConflict>();

        if (dayRowByEmployee == null) return (ignore, conflicts);

        foreach (var v in trackerEntries)
        {
            if (v.EmployeeId == null) continue;
            if (!dayRowByEmployee.TryGetValue(v.EmployeeId, out var row)) continue;
            if (!IsManualNonAbsenceDuty(row)) continue;

            if (!ignore.Add(v.Id)) continue;
            conflicts.Add(new AlTrackerConflict(
                date.ToString("yyyy-MM-dd"),
                v.EmployeeId,
                v.Id,
                v.FirstDay.ToString("yyyy-MM-dd"),
                v.LastDay.ToString("yyyy-MM-dd"),
                row!.ShiftType ?? "",
                row.AgentTask,
                row.ShiftStart,
                row.ShiftEnd,
                row.ManualChangeBy,
                // Shown as the Berlin wall clock (BerlinTime), like every other displayed instant.
                BerlinTime.Stamp(row.ManualChangeAt, "yyyy-MM-dd HH:mm")));
        }

        return (ignore, conflicts);
    }
}
