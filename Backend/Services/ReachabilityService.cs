using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GSDDashboard.API.Services;

public enum ReachabilityTier { VERY_EASY, EASY, MODERATE, HARD, IMPRACTICAL }

public record ReachabilityEntry(
    string FromCode,
    string ToCode,
    double DistanceKm,
    ReachabilityTier Tier,
    double? TravelMinutes,  // null — straight-line only, no routing API integrated
    bool? HasDirectTrain,   // null — straight-line only
    string Note
);

/// <summary>
/// Computes and caches haversine distance between every pair of active WIC locations.
/// Registered as Singleton so the 4-hour cache survives across requests.
/// Uses IServiceScopeFactory to resolve the scoped GSDContext at cache-fill time.
/// </summary>
public class ReachabilityService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private volatile List<ReachabilityEntry>? _cache;
    private volatile List<string>? _missingCoords;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ReachabilityService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public void InvalidateCache()
    {
        _cache = null;
        _cacheExpiry = DateTime.MinValue;
    }

    private static ReachabilityTier GetTier(double km) => km switch
    {
        <= 15  => ReachabilityTier.VERY_EASY,
        <= 40  => ReachabilityTier.EASY,
        <= 80  => ReachabilityTier.MODERATE,
        <= 150 => ReachabilityTier.HARD,
        _      => ReachabilityTier.IMPRACTICAL
    };

    public async Task<IReadOnlyList<ReachabilityEntry>> GetMatrixAsync()
    {
        if (_cache != null && DateTime.UtcNow < _cacheExpiry)
            return _cache;

        await _lock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cache != null && DateTime.UtcNow < _cacheExpiry)
                return _cache;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
            var locations = await db.WicLocations.Where(l => l.IsActive).ToListAsync();

            var entries = new List<ReachabilityEntry>(locations.Count * locations.Count);
            var missing = new List<string>();

            foreach (var a in locations)
            {
                if (string.IsNullOrEmpty(a.Coordinates))
                {
                    missing.Add(a.LocationCode);
                    continue;
                }
                foreach (var b in locations)
                {
                    if (a.LocationCode == b.LocationCode) continue;
                    if (string.IsNullOrEmpty(b.Coordinates)) continue;
                    var km = Haversine(a.Coordinates, b.Coordinates);
                    if (km == null) continue;
                    entries.Add(new ReachabilityEntry(
                        a.LocationCode, b.LocationCode,
                        Math.Round(km.Value, 1),
                        GetTier(km.Value),
                        null, null,
                        "straight-line only"));
                }
            }

            foreach (var code in missing)
                Console.Error.WriteLine(
                    $"[ReachabilityService] WARN: Reachability UNKNOWN for {code}: coordinates not geocoded.");

            _cache       = entries;
            _missingCoords = missing;
            _cacheExpiry = DateTime.UtcNow.AddHours(4);
            return _cache;
        }
        finally { _lock.Release(); }
    }

    public async Task<ReachabilityEntry?> GetAsync(string fromCode, string toCode)
    {
        var matrix = await GetMatrixAsync();
        return matrix.FirstOrDefault(e =>
            string.Equals(e.FromCode, fromCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ToCode,   toCode,   StringComparison.OrdinalIgnoreCase));
    }

    // Sanity check: Berlin → Munich expected ~504 km straight-line.
    public async Task<object> GetSanityCheckAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GSDContext>();
        var berlin = await db.WicLocations.FirstOrDefaultAsync(l =>
            l.IsActive && l.City != null && l.City.Contains("Berlin") && l.Coordinates != null);
        var munich = await db.WicLocations.FirstOrDefaultAsync(l =>
            l.IsActive && l.City != null &&
            (l.City.Contains("nchen") || l.City.Contains("Munich")) &&
            l.Coordinates != null);

        if (berlin == null || munich == null)
            return new { error = "Berlin or Munich WIC not found", berlinFound = berlin != null, munichFound = munich != null };

        var km = Haversine(berlin.Coordinates!, munich.Coordinates!);
        bool passed = km.HasValue && Math.Abs(km.Value - 504) < 80;

        return new
        {
            from        = berlin.LocationCode,
            to          = munich.LocationCode,
            fromCoords  = berlin.Coordinates,
            toCoords    = munich.Coordinates,
            distanceKm  = km.HasValue ? Math.Round(km.Value, 1) : (double?)null,
            expected    = "~504 km",
            passed,
            note        = "straight-line haversine only — no routing API"
        };
    }

    public async Task<IReadOnlyList<string>> GetMissingCoordsAsync()
    {
        await GetMatrixAsync(); // ensure cache is populated
        return _missingCoords ?? [];
    }

    private static double? Haversine(string coordA, string coordB)
    {
        var a = coordA.Split(','); var b = coordB.Split(',');
        if (a.Length < 2 || b.Length < 2) return null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (!double.TryParse(a[0].Trim(), System.Globalization.NumberStyles.Float, ci, out var lat1)) return null;
        if (!double.TryParse(a[1].Trim(), System.Globalization.NumberStyles.Float, ci, out var lon1)) return null;
        if (!double.TryParse(b[0].Trim(), System.Globalization.NumberStyles.Float, ci, out var lat2)) return null;
        if (!double.TryParse(b[1].Trim(), System.Globalization.NumberStyles.Float, ci, out var lon2)) return null;
        const double R  = 6371.0;
        var dLat    = (lat2 - lat1) * Math.PI / 180.0;
        var dLon    = (lon2 - lon1) * Math.PI / 180.0;
        var sinLat  = Math.Sin(dLat / 2);
        var sinLon  = Math.Sin(dLon / 2);
        var aVal    = sinLat * sinLat +
                      Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) * sinLon * sinLon;
        return R * 2 * Math.Atan2(Math.Sqrt(aVal), Math.Sqrt(1 - aVal));
    }
}

public static class ReachabilityEndpointMapper
{
    public static void MapReachabilityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wic/reachability/sanity",
            async (ReachabilityService svc) => Results.Ok(await svc.GetSanityCheckAsync()))
            .WithTags("WIC");

        app.MapGet("/api/wic/reachability",
            async (string? from, string? to, ReachabilityService svc) =>
        {
            if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
            {
                var entry = await svc.GetAsync(from, to);
                return entry == null
                    ? Results.NotFound(new { error = $"No entry for {from} -> {to}" })
                    : Results.Ok(entry);
            }
            var matrix = await svc.GetMatrixAsync();
            return Results.Ok(new { count = matrix.Count, entries = matrix });
        }).WithTags("WIC");
    }
}
