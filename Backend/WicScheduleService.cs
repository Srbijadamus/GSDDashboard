using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace GSDDashboard.API.Modules.WicSchedule;

public record WicDayDto(string Date, string DayOfWeek, string? SupportLocation, string? WicOpeningHours, string? WorkingShift, bool IsOnSite, bool IsOffDay, string? Task);
public record WicAgentScheduleDto(string EmployeeId, string FullName, string? TeamLeadName, List<string> AssignedLocations, List<WicDayDto> Days);
public record WicLocationDayDto(string Date, string DayOfWeek, bool IsOpen, string? OpenTime, string? CloseTime, string? OpenTime2, string? CloseTime2, string? RawSchedule, int AgentCount, List<string> AgentNames);
public record WicLocationScheduleDto(string LocationCode, string DisplayName, string? City, int TotalAssignedAgents, List<WicLocationDayDto> Days);
public record WicDayHoursDto(int DayOfWeek, string DayName, bool IsClosed, string? OpenTime, string? CloseTime, string? OpenTime2, string? CloseTime2, string? RawSchedule, int? MinRequired = null);
public record MinRequiredDto(int? Value);
public record WicOpeningHoursDto(string LocationCode, string DisplayName, string? City, int AssignedAgentCount, List<WicDayHoursDto> WeeklyHours);

public class WicScheduleService
{
    private readonly GSDContext _db;
    private readonly ILogger<WicScheduleService> _log;
    public WicScheduleService(GSDContext db, ILogger<WicScheduleService> log) { _db = db; _log = log; }

    private static string DayName(int d) => d switch {
        1 => "Mon", 2 => "Tue", 3 => "Wed", 4 => "Thu",
        5 => "Fri", 6 => "Sat", 7 => "Sun", _ => "?"
    };

    private static string GetDow(DateOnly d) => d.DayOfWeek switch {
        DayOfWeek.Monday => "Mon", DayOfWeek.Tuesday => "Tue",
        DayOfWeek.Wednesday => "Wed", DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri", DayOfWeek.Saturday => "Sat",
        DayOfWeek.Sunday => "Sun", _ => "?"
    };

    public async Task<List<WicAgentScheduleDto>> GetAgentScheduleAsync(string from, string to, string? employeeId)
    {
        if (!DateOnly.TryParse(from, out var fromDate)) fromDate = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(to,   out var toDate))   toDate   = fromDate.AddDays(13);

        var wicQuery = _db.WicShiftEntries.Where(w => w.ShiftDate >= fromDate && w.ShiftDate <= toDate);
        if (!string.IsNullOrEmpty(employeeId))
            wicQuery = wicQuery.Where(w => w.EmployeeId == employeeId);
        var wicEntries = await wicQuery.ToListAsync();

        var wicEmployeeIds = wicEntries.Select(w => w.EmployeeId).Distinct().ToList();
        if (!string.IsNullOrEmpty(employeeId)) wicEmployeeIds = new List<string> { employeeId };

        var employees = await _db.Employees
            .Where(e => wicEmployeeIds.Contains(e.EmployeeId) && e.IsActive)
            .ToListAsync();

        var assignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();

        var result = new List<WicAgentScheduleDto>();
        foreach (var emp in employees.OrderBy(e => e.TeamLeadName).ThenBy(e => e.FullName))
        {
            var empAssignments = assignments.Where(a => a.EmployeeName == emp.FullName).Select(a => a.LocationCode).ToList();
            var empEntries = wicEntries.Where(w => w.EmployeeId == emp.EmployeeId)
                .GroupBy(w => w.ShiftDate)
                .ToDictionary(g => g.Key, g =>
                {
                    if (g.Count() > 1)
                        _log.LogWarning("WicSchedule: {Count} WicShiftEntries for {EmpId} on {Date}",
                            g.Count(), emp.EmployeeId, g.Key);
                    return ShiftDuplicateResolver.BestWicEntry(g);
                });

            var days = new List<WicDayDto>();
            for (var d = fromDate; d <= toDate; d = d.AddDays(1))
            {
                empEntries.TryGetValue(d, out var entry);
                days.Add(new WicDayDto(d.ToString("yyyy-MM-dd"), GetDow(d),
                    entry?.SupportLocation, entry?.WicOpeningHours, entry?.WorkingShift,
                    entry?.IsOnSite ?? false, entry?.IsOffDay ?? false, entry?.Task));
            }

            result.Add(new WicAgentScheduleDto(emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
                emp.TeamLeadName, empAssignments, days));
        }
        return result;
    }

    public async Task<List<WicOpeningHoursDto>> GetOpeningHoursAsync(DateOnly? asOf = null)
    {
        var effectiveDate = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        var locations   = await _db.WicLocations.Where(l => l.IsActive).OrderBy(l => l.DisplayName).ToListAsync();
        var allHours    = await _db.WicOpeningHours.ToListAsync();
        var assignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();

        // Per location+day, pick the latest version whose EffectiveFrom <= today (NULL = from inception)
        var effectiveHours = allHours
            .Where(h => h.EffectiveFrom == null || h.EffectiveFrom <= effectiveDate)
            .GroupBy(h => (h.LocationCode, h.DayOfWeek))
            .Select(g => g.OrderByDescending(h => h.EffectiveFrom ?? DateOnly.MinValue).First())
            .ToList();

        return locations.Select(loc => {
            var locHours = effectiveHours
                .Where(h => h.LocationCode == loc.LocationCode)
                .OrderBy(h => h.DayOfWeek)
                .Select(h => new WicDayHoursDto(h.DayOfWeek, DayName(h.DayOfWeek), h.IsClosed,
                    h.OpenTime, h.CloseTime, h.OpenTime2, h.CloseTime2, h.RawSchedule, h.MinRequired)).ToList();
            int agentCount = assignments.Count(a =>
                a.LocationCode == loc.LocationCode ||
                a.LocationCode == loc.LocationCodeLegacy);
            return new WicOpeningHoursDto(loc.LocationCode, loc.DisplayName, loc.City, agentCount, locHours);
        }).ToList();
    }

    public async Task<string> ExportAgentsCsvAsync(string from, string to)
    {
        var data = await GetAgentScheduleAsync(from, to, null);
        if (!DateOnly.TryParse(from, out var fromDate)) fromDate = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(to,   out var toDate))   toDate   = fromDate.AddDays(13);

        var dates = new List<string>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
            dates.Add(d.ToString("dd.MM"));

        var lines = new List<string> { "Name,Team Lead,Standorte," + string.Join(",", dates) };

        foreach (var agent in data)
        {
            var cells = new List<string> {
                $"\"{agent.FullName}\"",
                $"\"{agent.TeamLeadName ?? ""}\"",
                $"\"{string.Join("|", agent.AssignedLocations)}\""
            };
            foreach (var day in agent.Days)
            {
                string cell = day.IsOffDay ? (day.WorkingShift ?? "OFF") :
                              day.IsOnSite && day.SupportLocation != null ? day.SupportLocation :
                              day.WorkingShift ?? "";
                cells.Add($"\"{cell}\"");
            }
            lines.Add(string.Join(",", cells));
        }
        return string.Join("\n", lines);
    }
}

public static class WicScheduleEndpointMapper
{
    public static void MapWicScheduleEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/wicschedule").WithTags("WicSchedule");

        grp.MapGet("/agents", async (string? from, string? to, string? employeeId, WicScheduleService svc) =>
            Results.Ok(await svc.GetAgentScheduleAsync(
                from ?? DateTime.Today.ToString("yyyy-MM-dd"),
                to   ?? DateTime.Today.AddDays(13).ToString("yyyy-MM-dd"), employeeId)));

        grp.MapGet("/opening-hours", async (WicScheduleService svc) =>
            Results.Ok(await svc.GetOpeningHoursAsync()));

        grp.MapPatch("/opening-hours/{locationCode}/{dow:int}/min-required",
            async (string locationCode, int dow, MinRequiredDto body, GSDDashboard.API.Data.GSDContext db) =>
        {
            var rows = await db.WicOpeningHours
                .Where(h => h.LocationCode == locationCode && h.DayOfWeek == dow)
                .ToListAsync();
            if (rows.Count == 0)
                return Results.NotFound(new { error = $"No opening-hours row for {locationCode} DOW={dow}" });
            foreach (var r in rows) r.MinRequired = body.Value;
            await db.SaveChangesAsync();
            return Results.Ok(new { locationCode, dow, minRequired = body.Value });
        });

        grp.MapGet("/export/agents/csv", async (string? from, string? to, WicScheduleService svc, HttpContext ctx) =>
        {
            var f = from ?? DateTime.Today.ToString("yyyy-MM-dd");
            var t = to   ?? DateTime.Today.AddDays(13).ToString("yyyy-MM-dd");
            var csv = await svc.ExportAgentsCsvAsync(f, t);
            var bytes = System.Text.Encoding.UTF8.GetBytes("\uFEFF" + csv);
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"WIC_Schedule_{f}_{t}.csv\"";
            return Results.File(bytes, "text/csv; charset=utf-8");
        });
    }
}
