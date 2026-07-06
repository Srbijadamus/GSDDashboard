using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Dashboard;

public record DashboardSummaryDto(
    string Date,
    int WorkingVoice,
    int WorkingChat,
    int WorkingSSP,
    int WorkingDispatcher,
    int OnAL,
    int OnSL,
    int OnTraining,
    int OnWicDuty,
    int OnPH,
    int OnOFF,
    int TotalActive,
    int WicUnoccupiedCount
);

public record TeamLeadSummaryDto(
    string TeamLeadName,
    int Working,
    int OnAL,
    int OnSL,
    int Training,
    int WicAssigned,
    int TotalAgents
);

public record WicAgentDto(
    string EmployeeId,
    string Name,
    string TeamLead,
    string? WorkingShift,
    string? WicOpeningHours
);

public record WicCardDto(
    string LocationCode,
    string DisplayName,
    string City,
    string Country,
    string? OpeningSchedule,
    string Status,
    List<WicAgentDto> Agents,
    int AgentCount
);

public class DashboardService
{
    private readonly GSDContext _db;
    public DashboardService(GSDContext db) => _db = db;

    public async Task<DashboardSummaryDto> GetSummaryAsync(DateOnly date)
    {
        var shifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date)
            .Join(_db.Employees,
                  s => s.EmployeeId,
                  e => e.EmployeeId,
                  (s, e) => new { Shift = s, Employee = e })
            .Where(x => x.Employee.IsActive)
            .ToListAsync();

        var workingVoice      = shifts.Count(x => x.Shift.ShiftType == ShiftTypes.Working      && x.Employee.PrimaryRole == "Voice");
        var workingChat       = shifts.Count(x => x.Shift.ShiftType == ShiftTypes.Working      && x.Employee.PrimaryRole is "Chat" or "Chat CRO");
        var workingSSP        = shifts.Count(x => x.Shift.ShiftType == ShiftTypes.Working      && x.Employee.PrimaryRole == "SSP");
        var workingDispatcher = shifts.Count(x => x.Shift.ShiftType == ShiftTypes.Working      && x.Employee.PrimaryRole is "Dispatcher" or "SME" or "Bulk PWs");
        var onAL              = shifts.Count(x => x.Shift.ShiftType is ShiftTypes.AnnualLeave or ShiftTypes.HalfAL);
        var onSL              = shifts.Count(x => x.Shift.ShiftType == ShiftTypes.SickLeave);
        var onTraining        = shifts.Count(x => x.Shift.ShiftType == ShiftTypes.Training);
        var onWicDuty         = shifts.Count(x => x.Shift.IsWicDuty);
        var onPH              = shifts.Count(x => x.Shift.ShiftType is ShiftTypes.PublicHol or ShiftTypes.LocalPH);
        var onOFF             = shifts.Count(x => x.Shift.ShiftType is ShiftTypes.Off or ShiftTypes.OffWeekend);
        var totalActive       = shifts.Count;

        var dow = (int)date.DayOfWeek;
        var openLocationCodes = await _db.WicOpeningHours
            .Where(h => h.DayOfWeek == dow && !h.IsClosed)
            .Select(h => h.LocationCode)
            .Distinct()
            .ToListAsync();

        var openDisplayNames = await _db.WicLocations
            .Where(l => l.IsActive && (openLocationCodes.Contains(l.LocationCode) ||
                        (l.LocationCodeLegacy != null && openLocationCodes.Contains(l.LocationCodeLegacy))))
            .Select(l => l.DisplayName)
            .ToListAsync();

        var openCities = await _db.WicLocations
            .Where(l => l.IsActive && (openLocationCodes.Contains(l.LocationCode) ||
                        (l.LocationCodeLegacy != null && openLocationCodes.Contains(l.LocationCodeLegacy))))
            .Select(l => l.City)
            .ToListAsync();

        var occupiedLocationsList = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date && w.IsOnSite)
            .Select(w => w.SupportLocation)
            .Distinct()
            .ToListAsync();

        var coveredCount = openDisplayNames.Count(d =>
            occupiedLocationsList.Any(o => o == d || openCities.Contains(o)));

        var wicUnoccupied = Math.Max(0, openLocationCodes.Count - coveredCount);

        return new DashboardSummaryDto(
            date.ToString("yyyy-MM-dd"),
            workingVoice, workingChat, workingSSP, workingDispatcher,
            onAL, onSL, onTraining, onWicDuty, onPH, onOFF,
            totalActive, wicUnoccupied
        );
    }

    public async Task<List<TeamLeadSummaryDto>> GetTeamLeadSummaryAsync(DateOnly date)
    {
        var employees = await _db.Employees
            .Where(e => e.IsActive && e.TeamLeadName != null)
            .ToListAsync();

        var shifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date)
            .ToListAsync();

        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date && w.IsOnSite)
            .ToListAsync();

        var shiftsByEmp = shifts.ToDictionary(s => s.EmployeeId);
        var wicByEmp    = wicEntries.ToDictionary(w => w.EmployeeId);

        return employees
            .GroupBy(e => e.TeamLeadName!.Trim())
            .Select(g =>
            {
                var working     = g.Count(e => shiftsByEmp.TryGetValue(e.EmployeeId, out var s) && s.ShiftType == ShiftTypes.Working);
                var onAL        = g.Count(e => shiftsByEmp.TryGetValue(e.EmployeeId, out var s) && s.ShiftType is ShiftTypes.AnnualLeave or ShiftTypes.HalfAL);
                var onSL        = g.Count(e => shiftsByEmp.TryGetValue(e.EmployeeId, out var s) && s.ShiftType == ShiftTypes.SickLeave);
                var training    = g.Count(e => shiftsByEmp.TryGetValue(e.EmployeeId, out var s) && s.ShiftType == ShiftTypes.Training);
                var wicAssigned = g.Count(e => wicByEmp.ContainsKey(e.EmployeeId));
                return new TeamLeadSummaryDto(g.Key, working, onAL, onSL, training, wicAssigned, g.Count());
            })
            .OrderByDescending(t => t.TotalAgents)
            .ToList();
    }

    public async Task<List<WicCardDto>> GetWicCardsAsync(DateOnly date)
    {
        var locations = await _db.WicLocations
            .Where(l => l.IsActive)
            .OrderBy(l => l.Country).ThenBy(l => l.City)
            .ToListAsync();

        var wicEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date)
            .Join(_db.Employees.Where(e => e.IsActive),
                  w => w.EmployeeId, e => e.EmployeeId,
                  (w, e) => new { Wic = w, Employee = e })
            .ToListAsync();

        var agentsByLocation = wicEntries
            .Where(x => x.Wic.IsOnSite)
            .GroupBy(x => x.Wic.SupportLocation ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.ToList());

        return locations.Select(loc =>
        {
            var locationAgents = agentsByLocation.TryGetValue(loc.DisplayName, out var agents) ? agents : [];
            if (!locationAgents.Any())
                locationAgents = agentsByLocation.TryGetValue(loc.City ?? "", out var cityAgents) ? cityAgents : [];

            var agentDtos = locationAgents.Select(x => new WicAgentDto(
                x.Employee.EmployeeId,
                x.Employee.FullName ?? x.Employee.EmployeeId,
                x.Employee.TeamLeadName?.Trim() ?? "",
                x.Wic.WorkingShift,
                x.Wic.WicOpeningHours
            )).ToList();

            return new WicCardDto(
                loc.LocationCode, loc.DisplayName,
                loc.City ?? "", loc.Country ?? "DE",
                loc.OpeningSchedule,
                agentDtos.Any() ? "OCCUPIED" : "UNOCCUPIED",
                agentDtos, agentDtos.Count
            );
        }).ToList();
    }
}

public static class DashboardEndpointMapper
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/dashboard").WithTags("Dashboard");

        grp.MapGet("/summary", async (string? date, DashboardService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetSummaryAsync(d));
        });

        grp.MapGet("/teamlead-summary", async (string? date, DashboardService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetTeamLeadSummaryAsync(d));
        });

        grp.MapGet("/wic-cards", async (string? date, DashboardService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetWicCardsAsync(d));
        });
    }
}


