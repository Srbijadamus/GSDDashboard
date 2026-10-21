using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

/// <summary>
/// Single source of truth for "is this WIC location closed by a public holiday on this
/// date". National rows are country-scoped: a null/empty <see cref="PublicHoliday.Country"/>
/// means German (the historical rows are all un-scoped German holidays) and matches only DE
/// locations; a "NL" row matches only NL locations. Regional rows apply only to DE locations
/// whose Bundesland (or PLZ-derived Bundesland) matches, case-insensitively.
/// </summary>
public static class HolidayMatcher
{
    /// <summary>Effective country of a holiday row: null/empty is German.</summary>
    public static string CountryOf(PublicHoliday h) =>
        string.IsNullOrWhiteSpace(h.Country) ? "DE" : h.Country.Trim();

    /// <summary>Does holiday <paramref name="h"/> apply to location <paramref name="loc"/> (date-independent)?</summary>
    public static bool Applies(PublicHoliday h, WicLocation loc)
    {
        if (h.IsNational)
            return string.Equals(CountryOf(h), loc.Country, StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(loc.Country, "DE", StringComparison.OrdinalIgnoreCase)) return false;
        var bundesland = loc.Bundesland ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country);
        return bundesland != null && string.Equals(h.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Is location <paramref name="loc"/> closed by a public holiday on <paramref name="date"/>?</summary>
    public static bool IsHoliday(WicLocation loc, DateOnly date, IReadOnlyCollection<PublicHoliday> holidays)
    {
        foreach (var h in holidays)
            if (h.HolidayDate == date && Applies(h, loc)) return true;
        return false;
    }
}
