using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.ALCalendar;

public record ALDayEntry(
    string EmployeeId,
    string FullName,
    string? TeamLeadName,
    string? Engagement,
    string Source,
    string FirstDay,
    string LastDay
);

public record ALCalendarDayDto(
    string Date,
    string DayName,
    int TotalOnAL,
    List<ALDayEntry> Agents,
    bool HasWarning,
    List<string> WarningTeams
);

public record ALCalendarDto(
    List<ALCalendarDayDto> Days,
    List<string> TeamLeads
);

public class ALCalendarService
{
    private readonly GSDContext _db;
    public ALCalendarService(GSDContext db) => _db = db;

    public async Task<ALCalendarDto> GetCalendarAsync(DateOnly from, DateOnly to, string? teamLead)
    {
        // Get vacations in range
        var vacations = await _db.Vacations
            .Where(v => v.FirstDay <= to && v.LastDay >= from)
            .Join(_db.Employees, v => v.EmployeeId, e => e.EmployeeId,
                (v, e) => new { v, e })
            .Where(x => !x.e.IsTrainee)
            .Where(x => teamLead == null || x.e.TeamLeadName == teamLead)
            .ToListAsync();

        // Get AL shifts in range
        var alShifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to &&
                        (s.ShiftType == "AL" || s.ShiftType == "HALF_AL"))
            .Join(_db.Employees, s => s.EmployeeId, e => e.EmployeeId,
                (s, e) => new { s, e })
            .Where(x => teamLead == null || x.e.TeamLeadName == teamLead)
            .ToListAsync();

        // Get all team leads for filter
        var teamLeads = await _db.Employees
            .Where(e => e.IsActive && e.TeamLeadName != null)
            .Select(e => e.TeamLeadName!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();

        // Get team sizes for warning calculation
        var teamSizes = await _db.Employees
            .Where(e => e.IsActive && e.TeamLeadName != null)
            .GroupBy(e => e.TeamLeadName!)
            .Select(g => new { Team = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Team, g => g.Count);

        var days = new List<ALCalendarDayDto>();

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var agentsOnAL = new List<ALDayEntry>();

            // From vacations
            foreach (var x in vacations)
            {
                if (x.v.FirstDay <= d && x.v.LastDay >= d)
                {
                    agentsOnAL.Add(new ALDayEntry(
                        x.e.EmployeeId,
                        x.e.FullName ?? x.e.EmployeeId,
                        x.e.TeamLeadName,
                        x.e.Engagement,
                        "VAC",
                        x.v.FirstDay.ToString("yyyy-MM-dd"),
                        x.v.LastDay.ToString("yyyy-MM-dd")
                    ));
                }
            }

            // From AL shifts (avoid duplicates)
            var vacEmpIds = agentsOnAL.Select(a => a.EmployeeId).ToHashSet();
            foreach (var x in alShifts)
            {
                if (x.s.ShiftDate == d && !vacEmpIds.Contains(x.e.EmployeeId))
                {
                    agentsOnAL.Add(new ALDayEntry(
                        x.e.EmployeeId,
                        x.e.FullName ?? x.e.EmployeeId,
                        x.e.TeamLeadName,
                        x.e.Engagement,
                        "SHIFT",
                        d.ToString("yyyy-MM-dd"),
                        d.ToString("yyyy-MM-dd")
                    ));
                }
            }

            // Coverage warnings — teams with >30% on AL
            var warningTeams = new List<string>();
            var byTeam = agentsOnAL.GroupBy(a => a.TeamLeadName ?? "Unknown");
            foreach (var tg in byTeam)
            {
                if (teamSizes.TryGetValue(tg.Key, out var size) && size > 0)
                {
                    var pct = (double)tg.Count() / size * 100;
                    if (pct > 30) warningTeams.Add(tg.Key);
                }
            }

            var dow = d.DayOfWeek.ToString()[..3];
            days.Add(new ALCalendarDayDto(
                d.ToString("yyyy-MM-dd"),
                dow,
                agentsOnAL.Count,
                agentsOnAL.OrderBy(a => a.TeamLeadName).ThenBy(a => a.FullName).ToList(),
                warningTeams.Count > 0,
                warningTeams
            ));
        }

        return new ALCalendarDto(days, teamLeads);
    }
}

public static class ALCalendarEndpointMapper
{
    public static void MapALCalendarEndpoints(this WebApplication app)
    {
        app.MapGet("/api/alcalendar", async (string? from, string? to, string? teamLead, ALCalendarService svc) =>
        {
            var f = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
            var t = to   != null && DateOnly.TryParse(to,   out var td) ? td : f.AddDays(13);
            return Results.Ok(await svc.GetCalendarAsync(f, t, teamLead));
        }).WithTags("ALCalendar");
    }
}
