using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Services;

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
        return all
            .Where(h =>
                h.LocationCode == locationCode &&
                h.DayOfWeek == dayOfWeek &&
                (h.EffectiveFrom == null || h.EffectiveFrom <= date))
            .OrderByDescending(h => h.EffectiveFrom ?? DateOnly.MinValue)
            .FirstOrDefault();
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
        return all
            .Where(h =>
                (h.LocationCode == locationCode ||
                 (locationCodeLegacy != null && h.LocationCode == locationCodeLegacy)) &&
                h.DayOfWeek == dayOfWeek &&
                (h.EffectiveFrom == null || h.EffectiveFrom <= date))
            .OrderByDescending(h => h.EffectiveFrom ?? DateOnly.MinValue)
            .FirstOrDefault();
    }
}
