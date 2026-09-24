using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.WicShifts;

public record WicCardDto2(
    string LocationCode,
    string DisplayName,
    string City,
    string Country,
    string? Address,
    bool IsNpp,
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
    string? WorkingShift,
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
        var dow = (int)date.DayOfWeek; // 0=Sun ... 6=Sat

        var locations = await _db.WicLocations
            .Where(l => l.IsActive)
            .Where(l => country == null || country == "ALL" || l.Country == country)
            .OrderBy(l => l.Country).ThenBy(l => l.City)
            .ToListAsync();

        var openingHours = await _db.WicOpeningHours
            .Where(h => h.DayOfWeek == dow)
            .ToListAsync();

        var sickToday = await _db.SickLeaves
            .Where(s => s.FirstDay <= date && s.LastDay >= date)
            .Select(s => s.EmployeeId)
            .ToListAsync();
        var alToday = await _db.Vacations
            .Where(v => v.FirstDay <= date && v.LastDay >= date)
            .Select(v => v.EmployeeId)
            .ToListAsync();
        var wicShifts = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date && w.IsOnSite)
            .Join(_db.Employees.Where(e => e.IsActive), w => w.EmployeeId, e => e.EmployeeId,
                  (w, e) => new { w, e })
            .ToListAsync();

        var assignments = await _db.WicAgentAssignments
            .Where(a => a.IsActive)
            .ToListAsync();

        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date)
            .ToListAsync();
        // Duplicate ShiftEntries per employee+date are possible (multiple import sources);
        // resolve to the most current row like the other services do.
        var shiftByEmp = shiftEntries
            .Where(s => s.EmployeeId != null)
            .GroupBy(s => s.EmployeeId!)
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));

        var cards = locations.Select(loc =>
        {
            var hours = WicHoursResolver.Resolve(openingHours, loc.LocationCode, dow, date);

            var locShifts = wicShifts
                .Where(x => WicLocationMatcher.MatchesSupportLocation(x.w.SupportLocation, loc))
                .ToList();

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
                .Where(a => WicLocationMatcher.MatchesAssignmentCode(a.LocationCode, loc) && a.AssignmentType == "MAIN")
                .Select(a => a.EmployeeName).ToList();

            var backupAgentNames = assignments
                .Where(a => WicLocationMatcher.MatchesAssignmentCode(a.LocationCode, loc) && a.AssignmentType == "BACKUP")
                .Select(a => a.EmployeeName).ToList();

            var agentList = new List<AssignedAgentDto>();
            var coveringInputs = new List<(string EmployeeId, string Name, string? ShiftStart, string? ShiftEnd, bool IsMain)>();
            foreach (var x in locShifts)
            {
                shiftByEmp.TryGetValue(x.e.EmployeeId, out var shiftEntry);
                // Absence detection mirrors the WIC assignment endpoint's blocking list
                // (AvailabilityResolver.BlockingAbsenceTypes): an agent whose ShiftEntry for
                // this date is an absence stays visible on the card (marked absent) but is
                // excluded from the coverage maths entirely.
                var absentType = shiftEntry?.ShiftType != null
                    && AvailabilityResolver.BlockingAbsenceTypes.Contains(shiftEntry.ShiftType)
                    ? shiftEntry.ShiftType.ToUpperInvariant() : null;
                var isSick = absentType == "SL" || sickToday.Contains(x.e.EmployeeId);
                var isAL = !isSick && (absentType is "AL" or "HALF_AL" || alToday.Contains(x.e.EmployeeId));
                var isAbsent = isSick || isAL || absentType != null;
                // Non-WIC work: agent has a ShiftEntry that is neither absence nor WIC_DUTY/HALF_AL
                // (e.g. WORKING with IsWicDuty=false → doing GSD work, not WIC duty)
                var isNonWic = !isSick && !isAL && shiftEntry != null
                    && !AvailabilityResolver.FullAbsenceTypes.Contains(shiftEntry.ShiftType)
                    && !string.Equals(shiftEntry.ShiftType, "HALF_AL", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(shiftEntry.ShiftType, "WIC_DUTY", StringComparison.OrdinalIgnoreCase);
                // Absent agents get their absence type as marker (SICK/AL/HALF_AL/OFF/...)
                // so RTM can see they are out; the frontend renders these as absent labels.
                var marker = isSick ? "SICK" : absentType == "HALF_AL" ? "HALF_AL" : isAL ? "AL"
                    : absentType ?? (isNonWic ? "GSD" : null);
                var shiftStart = marker
                    ?? (shiftEntry?.ShiftStart ?? x.w.WorkingShift?.Split('-').FirstOrDefault()?.Trim());
                var shiftEnd   = marker
                    ?? (shiftEntry?.ShiftEnd   ?? x.w.WorkingShift?.Split('-').LastOrDefault()?.Trim());
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

                var dto = new AssignedAgentDto(
                    x.e.EmployeeId,
                    x.e.FullName ?? x.e.EmployeeId,
                    x.e.TeamLeadName,
                    shiftStart, shiftEnd,
                    x.w.WorkingShift,
                    isMain, match, covered,
                    todaySchedule.TotalOpenMinutes,
                    note
                );
                agentList.Add(dto);
                // Absent agents stay on the card (marked absent) but are excluded from the
                // coverage calculation entirely: they neither cover nor drag the status down.
                if (!isAbsent)
                    coveringInputs.Add((dto.EmployeeId, dto.Name, dto.ShiftStart, dto.ShiftEnd, dto.IsMain));
            }

            var agentInputs = coveringInputs;

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
                loc.IsNpp,
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
