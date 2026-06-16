using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts; // CoverageStatus enum
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public record CoverageEvalResult(
    CoverageStatus Status,
    int PresentAgents,
    int RequiredAgents,
    string Reason
);

/// <summary>
/// THE canonical WIC coverage classifier.
/// All other files call Classify() or ClassifyByMinutes() — no other file may contain its own
/// COVERED/PARTIAL/UNCOVERED/CLOSED if-else chain.
/// </summary>
public class CoverageEvaluator
{
    private readonly GSDContext _db;
    private const int DefaultMin = 1;

    public CoverageEvaluator(GSDContext db) => _db = db;

    // ─── Canonical headcount-based classifier ───────────────────────────────────
    // Every other service delegates here instead of writing its own if-else chain.
    public static CoverageEvalResult Classify(
        bool isClosed,
        int presentAgents,
        int minRequired,
        string? closedReason = null)
    {
        if (isClosed)
            return new(CoverageStatus.CLOSED, 0, minRequired, closedReason ?? "CLOSED_DAY");
        if (presentAgents <= 0)
            return new(CoverageStatus.UNCOVERED, 0, minRequired,
                $"0/{minRequired} required agents present");
        if (presentAgents < minRequired)
            return new(CoverageStatus.PARTIAL, presentAgents, minRequired,
                $"{presentAgents}/{minRequired} required agents present");
        return new(CoverageStatus.COVERED, presentAgents, minRequired,
            $"{presentAgents}/{minRequired} agents present");
    }

    // ─── Minute-ratio classifier for CoverageCalculator ─────────────────────────
    // CoverageCalculator (minute-based schedule view) calls this so it has no own chain.
    public static CoverageStatus ClassifyByMinutes(bool isClosed, int coveredMinutes, int totalMinutes)
    {
        if (isClosed || totalMinutes == 0) return CoverageStatus.CLOSED;
        if (coveredMinutes <= 0)           return CoverageStatus.UNCOVERED;
        if (coveredMinutes < totalMinutes) return CoverageStatus.PARTIAL;
        return CoverageStatus.COVERED;
    }

    // ─── Full DB-backed evaluation ───────────────────────────────────────────────
    // Performs all DB lookups itself. Used by SubstitutionService and new standalone callers.
    // For bulk-loaded contexts (GetOpenAsync, GetWicSummaryAsync) use the static Classify() instead.
    public async Task<CoverageEvalResult> EvaluateAsync(
        string locationCode,
        DateOnly date,
        IReadOnlyList<string>? absentEmployeeIds = null)
    {
        absentEmployeeIds ??= [];

        var loc = await _db.WicLocations
            .FirstOrDefaultAsync(l => l.LocationCode == locationCode && l.IsActive);
        if (loc == null)
            return new(CoverageStatus.CLOSED, 0, 0, $"Location not found: {locationCode}");

        int minRequired = loc.MinAgentsRequired ?? DefaultMin;
        int dow = (int)date.DayOfWeek; // 0=Sun ... 6=Sat

        var hours = await _db.WicOpeningHours
            .FirstOrDefaultAsync(h => h.LocationCode == locationCode && h.DayOfWeek == dow);
        if (hours == null || hours.IsClosed)
            return Classify(true, 0, minRequired, "CLOSED_DAY");

        bool isNational = await _db.PublicHolidays
            .AnyAsync(ph => ph.HolidayDate == date && ph.IsNational);
        if (isNational)
            return Classify(true, 0, minRequired, "NATIONAL_HOLIDAY");

        string? bundesland = loc.Bundesland
            ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country);
        if (bundesland != null)
        {
            bool isRegional = await _db.PublicHolidays.AnyAsync(ph =>
                ph.HolidayDate == date &&
                string.Equals(ph.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase));
            if (isRegional)
                return Classify(true, 0, minRequired, $"REGIONAL_HOLIDAY ({bundesland})");
        }

        var onSiteEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date && w.IsOnSite)
            .ToListAsync();

        var scheduledIds = onSiteEntries
            .Where(w => WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc))
            .Select(w => w.EmployeeId)
            .ToList();

        var absentSet = new HashSet<string>(absentEmployeeIds, StringComparer.OrdinalIgnoreCase);
        int present = scheduledIds.Count(id => !absentSet.Contains(id));

        return Classify(false, present, minRequired);
    }
}
