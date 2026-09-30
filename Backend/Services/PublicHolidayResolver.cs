using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

/// <summary>
/// Single source of truth for "does this PublicHolidays row apply to this
/// employee's Bundesland" — national always applies; regional applies when
/// the Bundesland matches. Compares through Mojibake.Repair() on both sides
/// so a corrupted value in EITHER PublicHolidays.Bundesland or
/// Employees.Bundesland (this or a future import) still matches correctly,
/// instead of silently never firing the way the 2026 Baden-Württemberg row
/// did. Used by ShiftValidationService (R7), RosterService (roster
/// generation holiday skip) and PublicHolidayService — all three used to run
/// this comparison as a plain SQL-translated `==`, which is exactly what let
/// the mojibake disable the rule silently.
/// </summary>
public static class PublicHolidayResolver
{
    public static bool Applies(PublicHoliday h, string? bundesland)
    {
        if (h.IsNational) return true;
        if (string.IsNullOrEmpty(bundesland) || string.IsNullOrEmpty(h.Bundesland)) return false;
        return Mojibake.Repair(h.Bundesland) == Mojibake.Repair(bundesland);
    }

    /// <summary>Single-date lookup: the holiday for this date that applies to
    /// this Bundesland, national rows preferred over regional.</summary>
    public static PublicHoliday? Resolve(IEnumerable<PublicHoliday> holidaysOnDate, string? bundesland)
    {
        PublicHoliday? regional = null;
        foreach (var h in holidaysOnDate)
        {
            if (h.IsNational) return h;
            if (regional == null && Applies(h, bundesland)) regional = h;
        }
        return regional;
    }
}
