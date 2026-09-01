using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public class WicMigrationDryRunService(GSDContext db)
{
    public async Task<WicMigrationDryRunResult> RunAsync()
    {
        var locations = await db.WicLocations.Where(l => l.IsActive).ToListAsync();

        // All WicShiftEntries that claim the agent is on-site for WIC (not a GSD day, not off)
        var wicEntries = await db.WicShiftEntries
            .Where(w => w.IsOnSite && !w.IsGSDDay)
            .ToListAsync();

        // ShiftEntries indexed by (EmployeeId, ShiftDate)
        var shiftEntriesRaw = await db.ShiftEntries
            .Where(s => s.ShiftType != "WIC_DUTY")
            .ToListAsync();

        var shiftByKey = shiftEntriesRaw
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));

        int wouldMigrate      = 0;
        int noShiftEntry      = 0;     // already handled: no ShiftEntry → no conflict
        int alreadyWicDuty    = 0;     // ShiftType already WIC_DUTY — filtered out above but count anyway
        int isAbsence         = 0;     // ShiftType is SL/AL/UL/etc — do not migrate
        int locationNotFound  = 0;     // WicLocationMatcher returns false for all locations
        int isGsdDaySkipped   = 0;     // IsGSDDay=true rows (counted for completeness)

        var unmatchedLocations  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var migrateSamples      = new List<MigrationSampleRow>();

        // Count IsGSDDay=true rows (excluded from analysis but informative)
        isGsdDaySkipped = await db.WicShiftEntries.CountAsync(w => w.IsOnSite && w.IsGSDDay);

        foreach (var w in wicEntries)
        {
            // Find matching location
            var loc = locations.FirstOrDefault(l =>
                WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, l));

            if (loc == null)
            {
                locationNotFound++;
                if (w.SupportLocation != null)
                    unmatchedLocations.Add(w.SupportLocation);
                continue;
            }

            // Check ShiftEntry
            if (!shiftByKey.TryGetValue((w.EmployeeId, w.ShiftDate), out var sh))
            {
                // No ShiftEntry at all — nothing to migrate (WicShiftEntry alone is fine)
                noShiftEntry++;
                continue;
            }

            if (string.Equals(sh.ShiftType, "WIC_DUTY", StringComparison.OrdinalIgnoreCase))
            {
                alreadyWicDuty++;
                continue;
            }

            if (AvailabilityResolver.FullAbsenceTypes.Contains(sh.ShiftType) ||
                string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase))
            {
                isAbsence++;
                continue;
            }

            // This row qualifies: ShiftType=WORKING (or other non-WIC, non-absence) + valid WIC location
            wouldMigrate++;
            if (migrateSamples.Count < 20)
            {
                migrateSamples.Add(new MigrationSampleRow(
                    w.EmployeeId, w.ShiftDate.ToString("yyyy-MM-dd"),
                    sh.ShiftType, loc.DisplayName, w.SupportLocation));
            }
        }

        // Distinct unmatched SupportLocation values with counts
        var unmatchedWithCounts = wicEntries
            .Where(w => w.SupportLocation != null &&
                        !locations.Any(l => WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, l)))
            .GroupBy(w => w.SupportLocation!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new UnmatchedLocation(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        return new WicMigrationDryRunResult(
            TotalWicOnSiteRows:   wicEntries.Count,
            WouldMigrate:         wouldMigrate,
            NoShiftEntry:         noShiftEntry,
            AlreadyWicDuty:       alreadyWicDuty,
            IsAbsence:            isAbsence,
            LocationNotFound:     locationNotFound,
            IsGsdDaySkipped:      isGsdDaySkipped,
            UnmatchedLocations:   unmatchedWithCounts,
            Samples:              migrateSamples
        );
    }
}

public record WicMigrationDryRunResult(
    int TotalWicOnSiteRows,
    int WouldMigrate,
    int NoShiftEntry,
    int AlreadyWicDuty,
    int IsAbsence,
    int LocationNotFound,
    int IsGsdDaySkipped,
    List<UnmatchedLocation> UnmatchedLocations,
    List<MigrationSampleRow> Samples
);

public record UnmatchedLocation(string SupportLocation, int Count);
public record MigrationSampleRow(string EmployeeId, string ShiftDate, string CurrentShiftType, string MatchedLocation, string? RawSupportLocation);

public static class WicMigrationEndpoints
{
    public static void MapWicMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/wic-migration/dry-run",
            async (WicMigrationDryRunService svc) => Results.Ok(await svc.RunAsync()))
            .WithTags("Admin");
    }
}
