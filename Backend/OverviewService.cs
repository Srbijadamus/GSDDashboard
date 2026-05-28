using GSDDashboard.API.Data;
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
    public OverviewService(GSDContext db) => _db = db;

    public async Task<OverviewDetailDto> GetDetailAsync(string type, DateOnly date)
    {
        var query = _db.ShiftEntries
            .Where(s => s.ShiftDate == date)
            .Join(_db.Employees, s => s.EmployeeId, e => e.EmployeeId,
                (s, e) => new { Shift = s, Employee = e })
            .Where(x => x.Employee.IsActive);

        IQueryable<dynamic>? filtered = null;

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
    }
}
