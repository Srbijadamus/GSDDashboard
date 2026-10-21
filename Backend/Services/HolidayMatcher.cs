using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

/// <summary>
/// Single source of truth for "is this WIC location (or this Bundesland) closed by a
/// public holiday on this date". National rows are country-scoped: a null/empty
/// <see cref="PublicHoliday.Country"/> means German (the historical rows are all un-scoped
/// German holidays) and matches only DE; a "NL" row matches only NL. Regional rows apply
/// only to DE and match the Bundesland case-insensitively.
/// </summary>
public static class HolidayMatcher
{
    /// <summary>Effective country of a holiday row: null/empty is German.</summary>
    public static string CountryOf(PublicHoliday h) =>
        string.IsNullOrWhiteSpace(h.Country) ? "DE" : h.Country.Trim();

    /// <summary>
    /// Core matching: does holiday <paramref name="h"/> apply to a country + Bundesland
    /// (e.g. a WIC location, or an employee's Bundesland)?
    /// </summary>
    public static bool Applies(PublicHoliday h, string? country, string? bundesland)
    {
        if (h.IsNational)
            return string.Equals(CountryOf(h), country ?? "DE", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(country ?? "DE", "DE", StringComparison.OrdinalIgnoreCase)) return false;
        return !string.IsNullOrEmpty(bundesland) && !string.IsNullOrEmpty(h.Bundesland) &&
            string.Equals(h.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Location overload (WIC): derives the Bundesland from the column or the PLZ fallback.</summary>
    public static bool Applies(PublicHoliday h, WicLocation loc) =>
        Applies(h, loc.Country, loc.Bundesland ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country));

    /// <summary>Is location <paramref name="loc"/> closed by a public holiday on <paramref name="date"/>?</summary>
    public static bool IsHoliday(WicLocation loc, DateOnly date, IReadOnlyCollection<PublicHoliday> holidays)
    {
        foreach (var h in holidays)
            if (h.HolidayDate == date && Applies(h, loc)) return true;
        return false;
    }

    /// <summary>
    /// Employee overload (EMPLOYEE_HOLIDAY): is the given Bundesland (a German employee's
    /// Bundesland) on holiday on <paramref name="date"/>?
    /// </summary>
    public static bool IsHoliday(string? bundesland, DateOnly date, IReadOnlyCollection<PublicHoliday> holidays)
    {
        foreach (var h in holidays)
            if (h.HolidayDate == date && Applies(h, "DE", bundesland)) return true;
        return false;
    }
}
