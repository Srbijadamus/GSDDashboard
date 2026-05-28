using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.WicShifts;

public record WicCardDto2(
    string LocationCode,
    string DisplayName,
    string City,
    string Country,
    string? Address,
    TodaySchedule TodaySchedule,
    List<AssignedAgentDto> AssignedAgents,
    List<string> MainAgents,
    List<string> BackupAgents,
    string CoverageStatus,
    int CoveragePercent
);

public record TodaySchedule(
    bool IsClosed,
    string? OpenTime,
    string? CloseTime,
    string? OpenTime2,
    string? CloseTime2,
    int TotalOpenMinutes,
    string? RawSchedule
);

public record AssignedAgentDto(
    string EmployeeId,
    string Name,
    string? TeamLead,
    string? ShiftStart,
    string? ShiftEnd,
    bool IsMain,
    string CoverageMatch,
    int CoveredMinutes,
    int TotalOpenMinutes,
    string? MismatchNote
);

public class WicCardsService
{
    private readonly GSDContext _db;
    public WicCardsService(GSDContext db) => _db = db;

    public async Task<List<WicCardDto2>> GetCardsAsync(DateOnly date, string? country)
    {
        // Day of week: Mon=1 ... Sat=6, Sun=0
        var dow = (int)date.DayOfWeek == 0 ? 0 : (int)date.DayOfWeek;

        var locations = await _db.WicLocations
            .Where(l => l.IsActive)
            .Where(l => country == null || country == "ALL" || l.Country == country)
            .OrderBy(l => l.Country).ThenBy(l => l.City)
            .ToListAsync();

        var openingHours = await _db.WicOpeningHours
            .Where(h => h.DayOfWeek == dow)
            .ToListAsync();

        var wicShifts = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date && w.IsOnSite)
            .Join(_db.Employees, w => w.EmployeeId, e => e.EmployeeId,
                  (w, e) => new { w, e })
            .ToListAsync();

        var assignments = await _db.WicAgentAssignments
            .Where(a => a.IsActive)
            .ToListAsync();

        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date)
            .ToListAsync();

        var cards = locations.Select(loc =>
        {
            var hours = openingHours.FirstOrDefault(h => h.LocationCode == loc.LocationCode);
            var locShifts = wicShifts.Where(x => x.w.SupportLocation == loc.DisplayName ||
                                                  x.w.SupportLocation == loc.City).ToList();

            var todaySchedule = new TodaySchedule(
                IsClosed: hours == null || hours.IsClosed,
                OpenTime: hours?.OpenTime,
                CloseTime: hours?.CloseTime,
                OpenTime2: hours?.OpenTime2,
                CloseTime2: hours?.CloseTime2,
                TotalOpenMinutes: hours == null || hours.IsClosed ? 0 :
                    CoverageCalculator.CalcOpenMinutes(hours.OpenTime, hours.CloseTime,
                                                       hours.OpenTime2, hours.CloseTime2),
                RawSchedule: hours?.RawSchedule
            );

            var mainAgentNames = assignments
                .Where(a => a.LocationCode == loc.LocationCode && a.AssignmentType == "MAIN")
                .Select(a => a.EmployeeName).ToList();

            var backupAgentNames = assignments
                .Where(a => a.LocationCode == loc.LocationCode && a.AssignmentType == "BACKUP")
                .Select(a => a.EmployeeName).ToList();

            var agentList = locShifts.Select(x =>
            {
                var shiftEntry = shiftEntries.FirstOrDefault(s => s.EmployeeId == x.e.EmployeeId);
                var shiftStart = shiftEntry?.ShiftStart ?? x.w.WorkingShift?.Split('-').FirstOrDefault()?.Trim();
                var shiftEnd   = shiftEntry?.ShiftEnd   ?? x.w.WorkingShift?.Split('-').LastOrDefault()?.Trim();
                var isMain = mainAgentNames.Contains(x.e.FullName ?? "");

                var covered = todaySchedule.IsClosed ? 0 :
                    CoverageCalculator.CalcAgentCoverage(
                        shiftStart, shiftEnd,
                        hours?.OpenTime, hours?.CloseTime,
                        hours?.OpenTime2, hours?.CloseTime2);

                var match = todaySchedule.TotalOpenMinutes == 0 ? "NONE"
                          : covered >= todaySchedule.TotalOpenMinutes ? "FULL"
                          : covered > 0 ? "PARTIAL"
                          : "NONE";

                string? note = null;
                if (match == "PARTIAL" && !string.IsNullOrWhiteSpace(shiftStart) && !string.IsNullOrWhiteSpace(hours?.OpenTime))
                {
                    var aStart = CoverageCalculator.ToMinutes(shiftStart);
                    var cStart = CoverageCalculator.ToMinutes(hours.OpenTime);
                    if (aStart > cStart)
                        note = $"Misses {hours.OpenTime}-{shiftStart} ({aStart - cStart} min)";
                }

                return new AssignedAgentDto(
                    x.e.EmployeeId,
                    x.e.FullName ?? x.e.EmployeeId,
                    x.e.TeamLeadName,
                    shiftStart, shiftEnd,
                    isMain, match, covered,
                    todaySchedule.TotalOpenMinutes,
                    note
                );
            }).ToList();

            var agentInputs = agentList.Select(a =>
                (a.EmployeeId, a.Name, a.ShiftStart, a.ShiftEnd, a.IsMain)).ToList();

            var coverageResult = CoverageCalculator.Calculate(
                todaySchedule.IsClosed,
                hours?.OpenTime, hours?.CloseTime,
                hours?.OpenTime2, hours?.CloseTime2,
                agentInputs
            );

            return new WicCardDto2(
                loc.LocationCode,
                loc.DisplayName,
                loc.City ?? "",
                loc.Country ?? "DE",
                loc.FullAddress,
                todaySchedule,
                agentList,
                mainAgentNames,
                backupAgentNames,
                coverageResult.Status.ToString(),
                coverageResult.CoveragePercent
            );
        }).ToList();

        return cards;
    }
}

public static class WicCardsEndpointMapper
{
    public static void MapWicCardsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/wic/cards", async (string? date, string? country, WicCardsService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd)
                ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetCardsAsync(d, country));
        }).WithTags("WIC");
    }
}
