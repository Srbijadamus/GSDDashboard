using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Overview;

public record OverviewAgentDto(
    string EmployeeId,
    string FullName,
    string? TeamLeadName,
    string? Engagement,
    string? PrimaryRole,
    string? ShiftStart,
    string? ShiftEnd,
    string? AgentTask,
    string? LocationId,
    string ShiftType
);

public record OverviewDetailDto(
    string Type,
    int Count,
    List<OverviewAgentDto> Agents
);

public class OverviewService
{
    private readonly GSDContext _db;
    private readonly AvailabilityResolver _resolver;
    public OverviewService(GSDContext db, AvailabilityResolver resolver) { _db = db; _resolver = resolver; }

    // WIC status summary for the overview screen (Feature 4).
    // Returns, for each day in [date, date+horizon), each WIC with status + top substitute (if at risk).
    public async Task<object> GetWicSummaryAsync(string? dateStr, int horizon = 3)
    {
        horizon = Math.Clamp(horizon, 1, 7);
        var startDate = dateStr != null && DateOnly.TryParse(dateStr, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
        var endDate   = startDate.AddDays(horizon - 1);

        var locations      = await _db.WicLocations.Where(l => l.IsActive).OrderBy(l => l.Country).ThenBy(l => l.City).ToListAsync();
        var allHours       = await _db.WicOpeningHours.ToListAsync();
        var publicHolidays = await _db.PublicHolidays.ToListAsync();
        var allAssignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();
        var allEmployees   = await _db.Employees.Where(e => e.IsActive).ToListAsync();

        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= startDate && w.ShiftDate <= endDate)
            .ToListAsync();
        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => g.First());

        var days = new List<object>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            int dow = (int)date.DayOfWeek;
            bool isNationalHoliday = publicHolidays.Any(ph => ph.HolidayDate == date && ph.IsNational);

            var dayWicIds = wicEntries
                .Where(w => w.ShiftDate == date && w.IsOnSite)
                .Select(w => w.EmployeeId).Distinct().ToList();
            var absentToday = await _resolver.GetAbsentIdsAsync(dayWicIds, date);

            var locSummaries = locations.Select(loc =>
            {
                var hours = allHours.FirstOrDefault(h =>
                    (h.LocationCode == loc.LocationCode ||
                     (loc.LocationCodeLegacy != null && h.LocationCode == loc.LocationCodeLegacy)) &&
                    h.DayOfWeek == dow);
                string? bundesland = loc.Bundesland
                    ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country);
                bool isRegionalHoliday = bundesland != null &&
                    publicHolidays.Any(ph => ph.HolidayDate == date &&
                        string.Equals(ph.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase));
                bool isClosed = hours == null || hours.IsClosed || isNationalHoliday || isRegionalHoliday;

                var dayWic = wicEntries
                    .Where(w => w.ShiftDate == date && w.IsOnSite && WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc))
                    .ToList();

                // HALF_AL = 0.5 coverage (consistent with WicShiftService and SubstitutionService)
                var absent = new List<string>();
                double presentDouble = 0;
                foreach (var w in dayWic)
                {
                    if (absentToday.Contains(w.EmployeeId))
                    {
                        var emp = allEmployees.FirstOrDefault(e => e.EmployeeId == w.EmployeeId);
                        absent.Add(emp?.FullName ?? w.EmployeeId);
                    }
                    else
                    {
                        shiftByEmpDate.TryGetValue((w.EmployeeId, date), out var sh);
                        if (sh != null && string.Equals(sh.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase))
                            presentDouble += 0.5;
                        else
                            presentDouble += 1.0;
                    }
                }

                int eff = (int)Math.Floor(presentDouble);
                int minReq = loc.MinAgentsRequired ?? 1;

                string status = CoverageEvaluator.Classify(isClosed, eff, minReq).Status.ToString();

                var onWicIds = wicEntries.Where(w => w.ShiftDate == date && w.IsOnSite).Select(w => w.EmployeeId).ToHashSet();
                string? topSub = null;
                if (status is "PARTIAL" or "UNCOVERED")
                {
                    topSub = allEmployees
                        .Where(e => e.PrimaryRole == "SSP" &&
                               !onWicIds.Contains(e.EmployeeId) &&
                               shiftByEmpDate.TryGetValue((e.EmployeeId, date), out var sh) &&
                               sh.ShiftType != "SL" && sh.ShiftType != "AL" &&
                               sh.ShiftType != "HALF_AL" && sh.ShiftType != "UL")
                        .Select(e => e.FullName ?? e.EmployeeId)
                        .FirstOrDefault();
                }

                return new {
                    locationCode = loc.LocationCode,
                    displayName  = loc.DisplayName,
                    city         = loc.City,
                    country      = loc.Country,
                    status,
                    isOpen       = !isClosed,
                    scheduledCount   = dayWic.Count,
                    effectiveCoverage = eff,
                    minRequired  = minReq,
                    absentAgents = absent,
                    topSubstitute = topSub
                };
            }).ToList();

            int atRisk = locSummaries.Count(s => s.status is "PARTIAL" or "UNCOVERED");
            days.Add(new { date = date.ToString("yyyy-MM-dd"), dayOfWeek = date.DayOfWeek.ToString(), atRiskCount = atRisk, locations = locSummaries });
        }

        return new { horizon, startDate = startDate.ToString("yyyy-MM-dd"), days };
    }

    public async Task<OverviewDetailDto> GetDetailAsync(string type, DateOnly date)
    {
        var query = _db.ShiftEntries
            .Where(s => s.ShiftDate == date)
            .Join(_db.Employees, s => s.EmployeeId, e => e.EmployeeId,
                (s, e) => new { Shift = s, Employee = e })
            .Where(x => x.Employee.IsActive);

        var agents = type.ToUpper() switch
        {
            "VOICE" => await query
                .Where(x => x.Shift.ShiftType == "WORKING" && x.Employee.PrimaryRole != "SSP" &&
                            x.Employee.PrimaryRole != "Chat" && !x.Shift.IsWicDuty)
                .Select(x => new OverviewAgentDto(
                    x.Employee.EmployeeId, x.Employee.FullName ?? x.Employee.EmployeeId,
                    x.Employee.TeamLeadName, x.Employee.Engagement, x.Employee.PrimaryRole,
                    x.Shift.ShiftStart, x.Shift.ShiftEnd, x.Shift.AgentTask, x.Shift.LocationId, x.Shift.ShiftType))
                .ToListAsync(),

            "CHAT" => await query
                .Where(x => x.Shift.ShiftType == "WORKING" && x.Employee.PrimaryRole == "Chat")
                .Select(x => new OverviewAgentDto(
                    x.Employee.EmployeeId, x.Employee.FullName ?? x.Employee.EmployeeId,
                    x.Employee.TeamLeadName, x.Employee.Engagement, x.Employee.PrimaryRole,
                    x.Shift.ShiftStart, x.Shift.ShiftEnd, x.Shift.AgentTask, x.Shift.LocationId, x.Shift.ShiftType))
                .ToListAsync(),

            "BACKLOG" => await query
                .Where(x => x.Shift.ShiftType == "WORKING" && x.Employee.PrimaryRole == "SSP")
                .Select(x => new OverviewAgentDto(
                    x.Employee.EmployeeId, x.Employee.FullName ?? x.Employee.EmployeeId,
                    x.Employee.TeamLeadName, x.Employee.Engagement, x.Employee.PrimaryRole,
                    x.Shift.ShiftStart, x.Shift.ShiftEnd, x.Shift.AgentTask, x.Shift.LocationId, x.Shift.ShiftType))
                .ToListAsync(),

            "AL" => await query
                .Where(x => x.Shift.ShiftType == "AL" || x.Shift.ShiftType == "HALF_AL")
                .Select(x => new OverviewAgentDto(
                    x.Employee.EmployeeId, x.Employee.FullName ?? x.Employee.EmployeeId,
                    x.Employee.TeamLeadName, x.Employee.Engagement, x.Employee.PrimaryRole,
                    x.Shift.ShiftStart, x.Shift.ShiftEnd, x.Shift.AgentTask, x.Shift.LocationId, x.Shift.ShiftType))
                .ToListAsync(),

            "SL" => await query
                .Where(x => x.Shift.ShiftType == "SL")
                .Select(x => new OverviewAgentDto(
                    x.Employee.EmployeeId, x.Employee.FullName ?? x.Employee.EmployeeId,
                    x.Employee.TeamLeadName, x.Employee.Engagement, x.Employee.PrimaryRole,
                    x.Shift.ShiftStart, x.Shift.ShiftEnd, x.Shift.AgentTask, x.Shift.LocationId, x.Shift.ShiftType))
                .ToListAsync(),

            "TRAINING" => await query
                .Where(x => x.Shift.ShiftType == "TRAINING")
                .Select(x => new OverviewAgentDto(
                    x.Employee.EmployeeId, x.Employee.FullName ?? x.Employee.EmployeeId,
                    x.Employee.TeamLeadName, x.Employee.Engagement, x.Employee.PrimaryRole,
                    x.Shift.ShiftStart, x.Shift.ShiftEnd, x.Shift.AgentTask, x.Shift.LocationId, x.Shift.ShiftType))
                .ToListAsync(),

            "WIC" => await query
                .Where(x => x.Shift.IsWicDuty || x.Shift.ShiftType == "WIC_DUTY")
                .Select(x => new OverviewAgentDto(
                    x.Employee.EmployeeId, x.Employee.FullName ?? x.Employee.EmployeeId,
                    x.Employee.TeamLeadName, x.Employee.Engagement, x.Employee.PrimaryRole,
                    x.Shift.ShiftStart, x.Shift.ShiftEnd, x.Shift.AgentTask, x.Shift.LocationId, x.Shift.ShiftType))
                .ToListAsync(),

            _ => new List<OverviewAgentDto>()
        };

        return new OverviewDetailDto(type, agents.Count, agents.OrderBy(a => a.TeamLeadName).ThenBy(a => a.FullName).ToList());
    }
}

public static class OverviewEndpointMapper
{
    public static void MapOverviewEndpoints(this WebApplication app)
    {
        app.MapGet("/api/overview/detail", async (string type, string? date, OverviewService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetDetailAsync(type, d));
        }).WithTags("Overview");

        app.MapGet("/api/overview/wic-status", async (string? date, int? horizon, OverviewService svc) =>
            Results.Ok(await svc.GetWicSummaryAsync(date, horizon ?? 3))).WithTags("Overview");
    }
}
