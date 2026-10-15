using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts; // CoverageStatus (CoverageCalculator.cs) — shared infra, not WicCoverageGapService
using GSDDashboard.API.Modules.Shifts;    // AgentTasks.IsNonLocationValue — task-badge vs. centre-name test
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public record ForecastDayDto(
    string Date,
    string DayOfWeek,
    bool IsOpen,
    string? ClosedReason,
    int ScheduledCount,
    int EffectiveCoverage,
    int MinRequired,
    string Status,
    bool IsAtRisk,
    int CoverageBuffer   // effectiveCoverage - minRequired; negative means gap
);

public record ForecastLocationDto(
    string LocationCode,
    string DisplayName,
    string City,
    string Country,
    string? Coordinates,
    bool IsNpp,
    string TodayStatus,
    List<ForecastDayDto> Forecast,
    int AtRiskDays
);

public record ForecastResponse(
    string GeneratedAt,
    int Horizon,
    int LocationCount,
    int TotalAtRiskDays,
    List<ForecastLocationDto> Locations
);

public class ForecastService
{
    private readonly GSDContext _db;

    // Absent/non-WIC logic is centralised in AvailabilityResolver.GetWicContribution.

    public ForecastService(GSDContext db) => _db = db;

    // Location matching, the "is this row WIC duty here" reading and the union of the two
    // cover signals all come from the shared primitives (WicLocationMatcher.MatchesAgentTask,
    // AvailabilityResolver.IsWicDutyAt, WicPresenceResolver.Resolve). That is NOT the
    // coupling that was reverted on 2026-09-30: coverage-gaps and forecast stay two
    // independent features measuring two different things (assignment-structure gap
    // classification vs. headcount-vs-MinRequired risk), and neither calls into the other.
    // What they may not each own a private copy of is how coverage is READ - five surfaces
    // with five readings is what put them out of step, twice.

    public async Task<ForecastResponse> GetForecastAsync(int horizon, string? locationCode, DateOnly? fromDate = null)
    {
        // Ceiling is the widest horizon the Overview topbar offers (90d). Beyond the
        // generated roster horizon the response is simply sparse; it is never padded.
        horizon = Math.Clamp(horizon, 1, 90);
        var today     = DateOnly.FromDateTime(DateTime.Today);
        var startDate = fromDate ?? today;
        var endDate   = startDate.AddDays(horizon - 1);

        var locations = await _db.WicLocations
            .Where(l => l.IsActive)
            .Where(l => locationCode == null || l.LocationCode == locationCode)
            .OrderBy(l => l.Country).ThenBy(l => l.City)
            .ToListAsync();

        var allHours       = await _db.WicOpeningHours.ToListAsync();
        var publicHolidays = await _db.PublicHolidays
            .Where(ph => ph.HolidayDate >= startDate && ph.HolidayDate <= endDate)
            .ToListAsync();
        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= startDate && w.ShiftDate <= endDate)
            .Join(_db.Employees.Where(e => e.IsActive && !TestAccounts.ExcludedEmployeeIds.Contains(e.EmployeeId)),
                  w => w.EmployeeId, e => e.EmployeeId, (w, e) => w)
            .ToListAsync();
        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));
        // Bulk-load sick leaves covering the forecast window for cross-check
        var sickLeaves = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && sl.FirstDay <= endDate && sl.LastDay >= startDate)
            .ToListAsync();

        // Signal-2 candidates for WicPresenceResolver: rows whose AgentTask could name a
        // centre, plus every AUTOMATIC back-office day (owner rule 2026-10-15: WIC has
        // priority over BO). A row counts as coverage even when the paired WicShiftEntries
        // on-site row is missing (the SYSTEM_WICFIX gap - 521 rows across 37 locations as of
        // 2026-09-30); the resolver reports that as Evidence=ROSTER_ONLY.
        var wicDutyEntriesAll = WicPresenceResolver.CandidateDutyRows(shiftEntries)
            .Where(s => !TestAccounts.IsExcluded(s.EmployeeId)).ToList();
        // WIC-over-BO: the agent -> priority-centre index the resolver needs to place a plan
        // BO day at the right centre.
        var activeEmployees = await _db.Employees
            .Where(e => e.IsActive && !TestAccounts.ExcludedEmployeeIds.Contains(e.EmployeeId))
            .ToListAsync();
        var activeAssignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();
        var boIndex = WicBackOfficePriority.BuildIndex(activeAssignments, locations, activeEmployees);

        var locationResults = new List<ForecastLocationDto>();

        foreach (var loc in locations)
        {
            var days = new List<ForecastDayDto>();

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                int dow = (int)date.DayOfWeek; // 0=Sun...6=Sat

                var hours = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy, dow, date);

                bool isNational = publicHolidays.Any(ph => ph.HolidayDate == date && ph.IsNational);
                string? bundesland = loc.Bundesland
                    ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country);
                bool isRegional = bundesland != null && publicHolidays.Any(ph =>
                    ph.HolidayDate == date &&
                    string.Equals(ph.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase));

                bool isClosed = hours == null || hours.IsClosed || isNational || isRegional;
                string? closedReason = isClosed
                    ? (isNational || isRegional ? "PUBLIC_HOLIDAY" : "CLOSED_DAY")
                    : null;

                int minReq = hours?.MinRequired ?? loc.MinAgentsRequired ?? 1;

                if (isClosed)
                {
                    days.Add(new ForecastDayDto(
                        date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(),
                        false, closedReason, 0, 0, minReq, "CLOSED", false, 0 - minReq));
                    continue;
                }

                // Both cover signals, unioned and deduplicated in ONE place for every
                // surface (WicPresenceResolver). This used to be forecast's own inline
                // union, which is why Briefing/Cards - which never had it - disagreed.
                var presence = WicPresenceResolver.Resolve(
                    loc, date, wicEntries, shiftByEmpDate, wicDutyEntriesAll,
                    empId => sickLeaves.Any(sl =>
                        sl.EmployeeId == empId && sl.FirstDay <= date && sl.LastDay >= date),
                    boIndex, allHours);

                double presentDouble = presence.PresentDouble;
                int scheduledCount = presence.OnSiteCount + presence.CheckInNotRecordedCount;

                int effectiveCoverage = (int)Math.Floor(presentDouble);
                var evalResult = CoverageEvaluator.Classify(false, effectiveCoverage, minReq);
                string status = evalResult.Status.ToString();
                bool isAtRisk = evalResult.Status is CoverageStatus.UNCOVERED or CoverageStatus.PARTIAL;

                days.Add(new ForecastDayDto(
                    date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(),
                    true, null, scheduledCount, effectiveCoverage, minReq,
                    status, isAtRisk, effectiveCoverage - minReq));
            }

            int atRiskDays = days.Count(d => d.IsAtRisk);
            var todayStr = today.ToString("yyyy-MM-dd");
            var todayDay = days.FirstOrDefault(d => d.Date == todayStr);
            string todayStatus = todayDay?.Status ?? "N/A";
            locationResults.Add(new ForecastLocationDto(
                loc.LocationCode, loc.DisplayName, loc.City ?? "", loc.Country ?? "DE",
                loc.Coordinates, loc.IsNpp, todayStatus, days, atRiskDays));
        }

        int totalAtRisk = locationResults.Sum(l => l.AtRiskDays);
        return new ForecastResponse(
            today.ToString("yyyy-MM-dd"),
            horizon,
            locationResults.Count,
            totalAtRisk,
            locationResults);
    }
}

public static class ForecastEndpointMapper
{
    public static void MapForecastEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wic/forecast", async (int? horizon, string? locationCode, string? startDate, ForecastService svc) =>
        {
            DateOnly? fromDate = null;
            if (startDate != null && DateOnly.TryParse(startDate, out var d))
                fromDate = d;
            return Results.Ok(await svc.GetForecastAsync(horizon ?? 28, locationCode, fromDate));
        }).WithTags("WIC");
    }
}
