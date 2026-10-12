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
    int CoveragePercent,
    // How many of this card's covering agents have no on-site row. > 0 drives the
    // "Staffed - check-in not recorded" badge on the card. The card is NOT uncovered:
    // these agents are rostered and count toward coverage like any other.
    int CheckInNotRecordedCount,
    // NONE / ON_SITE / ROSTER_ONLY - see WicCoverageEvidence.
    string CoverageEvidence,
    // How many of this card's covering agents are here by a manual decision.
    int ManualCount
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
    string? MismatchNote,
    // Rostered for WIC duty here (ShiftEntries) with no on-site WicShiftEntries row to
    // confirm it. The agent IS staffing the centre; only the check-in record is missing.
    bool CheckInNotRecorded = false,
    // A human deliberately set this duty (ad-hoc deployment, hand-edited shift, manual
    // assignment). Shown as a neutral "Manual" label with who/when in the tooltip —
    // never a warning, never an error.
    bool IsManualChange = false,
    string? ManualChangeBy = null,
    string? ManualChangeAt = null
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

        // Weekday rows plus this date's manual exception day (a Rollout day projects one).
        // Without the ExceptionDate arm the pre-filter by weekday alone would hide the
        // exception row from the resolver below and the centre would read as CLOSED.
        var openingHours = await _db.WicOpeningHours
            .Where(h => h.DayOfWeek == dow || h.ExceptionDate == date)
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
            .Join(_db.Employees.Where(e => e.IsActive && !TestAccounts.ExcludedEmployeeIds.Contains(e.EmployeeId)),
                  w => w.EmployeeId, e => e.EmployeeId, (w, e) => new { w, e })
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
        // Keyed the way WicPresenceResolver expects (employee + date).
        var shiftByEmpDate = shiftByEmp.ToDictionary(kv => (kv.Key, date), kv => kv.Value);
        // How many on-site centres this employee holds TODAY. More than one means the
        // day-level ShiftEntries row (one row per agent-day: UQ_ShiftEntries_EmpDate)
        // cannot describe every centre, so each card must use its own WicShiftEntries
        // time instead of that single row.
        var onSiteCountByEmployee = wicShifts
            .GroupBy(x => x.e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Count());
        // Signal-2 candidates. Cards had NO second signal, which is why 19 staffed centres
        // showed UNCOVERED with 0 agents listed on 2026-10-01.
        var candidateDutyRows = WicPresenceResolver.CandidateDutyRows(shiftEntries)
            .Where(s => !TestAccounts.IsExcluded(s.EmployeeId)).ToList();
        var activeEmpById = await _db.Employees
            .Where(e => e.IsActive && !TestAccounts.ExcludedEmployeeIds.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e);

        var cards = locations.Select(loc =>
        {
            // (E) locationCodeLegacy included — a centre whose opening hours live under the
            // legacy bridge code used to resolve to "no hours row" here and render as
            // CLOSED, while ForecastService (which passes the legacy code) showed it open.
            var hours = WicHoursResolver.Resolve(openingHours, loc.LocationCode, loc.LocationCodeLegacy, dow, date);

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
                // The assignment time AT THIS LOCATION. WicShiftEntries is unique per
                // (employee, date, location) and each row carries the time assigned to that
                // centre, so when the agent covers more than one centre that day the row's
                // own WorkingShift is the only time that describes THIS card. The ShiftEntries
                // row is one per agent-day (UQ_ShiftEntries_EmpDate) and holds a single
                // window, which is exactly what made the second Demmin centre read as
                // uncovered ("not on shift") in the dual-centre report.
                // Single-centre days are untouched: there the day row IS this centre's
                // assignment, and every legacy/imported row keeps the time it always had.
                var multiLocationDay = onSiteCountByEmployee.TryGetValue(x.e.EmployeeId, out var centreCount)
                    && centreCount > 1;
                var (locStart, locEnd) = multiLocationDay
                    ? SplitWorkingShift(x.w.WorkingShift) : (null, null);
                var shiftStart = marker
                    ?? locStart ?? shiftEntry?.ShiftStart ?? x.w.WorkingShift?.Split('-').FirstOrDefault()?.Trim();
                var shiftEnd   = marker
                    ?? locEnd   ?? shiftEntry?.ShiftEnd   ?? x.w.WorkingShift?.Split('-').LastOrDefault()?.Trim();
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
                    note,
                    CheckInNotRecorded: false,
                    IsManualChange: shiftEntry?.IsManualChange ?? false,
                    ManualChangeBy: shiftEntry?.ManualChangeBy,
                    // Stored UTC, shown as the Berlin wall clock the RTM actually worked in
                    // (see BerlinTime; the DB value is not changed).
                    ManualChangeAt: BerlinTime.Stamp(shiftEntry?.ManualChangeAt, "yyyy-MM-dd HH:mm")
                );
                agentList.Add(dto);
                // Absent agents stay on the card (marked absent) but are excluded from the
                // coverage calculation entirely: they neither cover nor drag the status down.
                if (!isAbsent)
                    coveringInputs.Add((dto.EmployeeId, dto.Name, dto.ShiftStart, dto.ShiftEnd, dto.IsMain));
            }

            // ── Signal 2: rostered here per ShiftEntries, no on-site row ───────────
            // These agents used to be invisible to this page entirely: the card showed
            // UNCOVERED with an empty agent list and the Overview offered a substitute for
            // a centre that was actually staffed. They are real rostered duties, so they
            // join the agent list AND the minute-based coverage maths using their
            // ShiftEntries times - the only times such a row has.
            var presence = WicPresenceResolver.Resolve(
                loc, date, wicShifts.Select(x => x.w), shiftByEmpDate, candidateDutyRows,
                empId => sickToday.Contains(empId));

            // Select by "rostered here with no on-site row", NOT by CheckInNotRecorded:
            // a MANUAL duty is deliberately exempt from that warning, and selecting on it
            // would drop the agent from the coverage maths altogether.
            foreach (var pa in presence.Agents.Where(p => p.Contribution > 0 && !p.HasOnSiteRow))
            {
                if (!activeEmpById.TryGetValue(pa.EmployeeId, out var emp)) continue;
                var sh = pa.Shift;
                var start = sh?.ShiftStart;
                var end   = sh?.ShiftEnd;
                var cov = todaySchedule.IsClosed ? 0 :
                    CoverageCalculator.CalcAgentCoverage(
                        start, end, hours?.OpenTime, hours?.CloseTime, hours?.OpenTime2, hours?.CloseTime2);
                var m = todaySchedule.TotalOpenMinutes == 0 ? "NONE"
                      : cov >= todaySchedule.TotalOpenMinutes ? "FULL"
                      : cov > 0 ? "PARTIAL" : "NONE";
                var isMainAgent = mainAgentNames.Contains(emp.FullName ?? "");
                agentList.Add(new AssignedAgentDto(
                    emp.EmployeeId, emp.FullName ?? emp.EmployeeId, emp.TeamLeadName,
                    start, end, null, isMainAgent, m, cov,
                    todaySchedule.TotalOpenMinutes, null,
                    CheckInNotRecorded: !(sh?.IsManualChange ?? false),
                    IsManualChange: sh?.IsManualChange ?? false,
                    ManualChangeBy: sh?.ManualChangeBy,
                    ManualChangeAt: BerlinTime.Stamp(sh?.ManualChangeAt, "yyyy-MM-dd HH:mm")));
                coveringInputs.Add((emp.EmployeeId, emp.FullName ?? emp.EmployeeId, start, end, isMainAgent));
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
                coverageResult.CoveragePercent,
                presence.CheckInNotRecordedCount,
                presence.Evidence.ToString(),
                presence.ManualCount
            );
        }).ToList();

        return cards;
    }

    /// <summary>
    /// First start / last end of a WicShiftEntries WorkingShift value. Handles single-block
    /// ("13:00-15:00") and two-block ("07:00-12:00 / 12:30-13:30") strings the same way
    /// WicShiftService.CreateAssignmentAsync does: the working window spans the first block's
    /// start through the last block's end. Fewer than two HH:MM tokens = unknown (null), so
    /// the caller keeps its existing fallback.
    /// </summary>
    private static (string? Start, string? End) SplitWorkingShift(string? workingShift)
    {
        if (string.IsNullOrWhiteSpace(workingShift)) return (null, null);
        var times = System.Text.RegularExpressions.Regex.Matches(workingShift, @"\d{1,2}:\d{2}");
        return times.Count >= 2 ? (times[0].Value, times[^1].Value) : (null, null);
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
