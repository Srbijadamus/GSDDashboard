using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts;
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

    private static readonly HashSet<string> _fullAbsenceTypes =
        new(StringComparer.OrdinalIgnoreCase) { "SL", "AL", "UL", "OL", "PH", "LPH", "RESIGNED" };

    public ForecastService(GSDContext db) => _db = db;

    public async Task<ForecastResponse> GetForecastAsync(int horizon, string? locationCode)
    {
        horizon = Math.Clamp(horizon, 1, 30);
        var today     = DateOnly.FromDateTime(DateTime.Today);
        var startDate = today;
        var endDate   = today.AddDays(horizon - 1);

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
            .Join(_db.Employees.Where(e => e.IsActive), w => w.EmployeeId, e => e.EmployeeId, (w, e) => w)
            .ToListAsync();
        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => g.First());
        // Bulk-load sick leaves covering the forecast window for cross-check
        var sickLeaves = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && sl.FirstDay <= endDate && sl.LastDay >= startDate)
            .ToListAsync();

        var locationResults = new List<ForecastLocationDto>();

        foreach (var loc in locations)
        {
            var days = new List<ForecastDayDto>();

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                int dow = (int)date.DayOfWeek; // 0=Sun...6=Sat

                var hours = allHours.FirstOrDefault(h =>
                    (h.LocationCode == loc.LocationCode ||
                     (loc.LocationCodeLegacy != null && h.LocationCode == loc.LocationCodeLegacy)) &&
                    h.DayOfWeek == dow);

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

                int minReq = loc.MinAgentsRequired ?? 1;

                if (isClosed)
                {
                    days.Add(new ForecastDayDto(
                        date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(),
                        false, closedReason, 0, 0, minReq, "CLOSED", false, 0 - minReq));
                    continue;
                }

                var dayWic = wicEntries
                    .Where(w => w.ShiftDate == date && w.IsOnSite &&
                                WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc))
                    .ToList();

                int fullAbsentCount = 0;
                double presentDouble = 0;
                foreach (var w in dayWic)
                {
                    shiftByEmpDate.TryGetValue((w.EmployeeId, date), out var sh);
                    bool isSick = sickLeaves.Any(sl =>
                        sl.EmployeeId == w.EmployeeId && sl.FirstDay <= date && sl.LastDay >= date);
                    if (isSick || (sh != null && _fullAbsenceTypes.Contains(sh.ShiftType)))
                        fullAbsentCount++;
                    else if (sh != null && string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase))
                        presentDouble += 0.5;
                    else
                        presentDouble += 1.0;
                }

                int effectiveCoverage = (int)Math.Floor(presentDouble);
                var evalResult = CoverageEvaluator.Classify(false, effectiveCoverage, minReq);
                string status = evalResult.Status.ToString();
                bool isAtRisk = evalResult.Status is CoverageStatus.UNCOVERED or CoverageStatus.PARTIAL;

                days.Add(new ForecastDayDto(
                    date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(),
                    true, null, dayWic.Count, effectiveCoverage, minReq,
                    status, isAtRisk, effectiveCoverage - minReq));
            }

            int atRiskDays = days.Count(d => d.IsAtRisk);
            var todayStr = today.ToString("yyyy-MM-dd");
            var todayDay = days.FirstOrDefault(d => d.Date == todayStr);
            string todayStatus = todayDay?.Status ?? "CLOSED";
            locationResults.Add(new ForecastLocationDto(
                loc.LocationCode, loc.DisplayName, loc.City ?? "", loc.Country ?? "DE",
                loc.Coordinates, todayStatus, days, atRiskDays));
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
        app.MapGet("/api/wic/forecast", async (int? horizon, string? locationCode, ForecastService svc) =>
            Results.Ok(await svc.GetForecastAsync(horizon ?? 28, locationCode))
        ).WithTags("WIC");
    }
}
