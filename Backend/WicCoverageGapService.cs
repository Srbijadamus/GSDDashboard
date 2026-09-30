using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.WicShifts;

// ── WIC coverage gaps (GET /api/wic/coverage-gaps) ───────────────────────────
// A day is a candidate gap for an ACTIVE WicLocation when the location is OPEN
// that day (WicHoursResolver, EffectiveFrom + legacy-code aware), it is not a
// national public holiday, the location has at least one active
// WicAgentAssignment (MAIN, BACKUP or REGIONAL), and nobody is actually
// rostered there that day. "Rostered there" (the cover signal) is either
// source the rest of the codebase already treats as authoritative:
//   - a WicShiftEntries row for this location+date with IsOnSite=1, or
//   - a ShiftEntries row (any employee) with ShiftType=WIC_DUTY whose
//     AgentTask resolves to this location's DisplayName (exact, or the
//     20-char-truncated prefix WicShiftService.CreateAssignmentAsync writes —
//     same match WicScheduleService.GetClosedDayDutiesAsync uses for orphaned
//     WIC_DUTY rows).
// A raw "WORKING" ShiftEntry proves nothing either way — it only means the
// agent is clocked in somewhere, not that they're covering this site — so it
// is never treated as cover.
//
// MAIN/BACKUP vs. REGIONAL: REGIONAL assignments are shared roaming backups
// that can cover several locations at once (one agent has been seen covering
// 7). Their absence must never fan out and flag every location they float
// for, so only MAIN/BACKUP ("the location's own agents") drive
// classification. The one exception: a location with no MAIN/BACKUP at all —
// REGIONAL is genuinely its only cover — falls back to using REGIONAL for
// classification. Either way, on every gap row the REGIONAL floaters who are
// NOT absent that day are listed separately as "available floaters" — a hint
// for RTM on who to grab — without changing the category.
//
// Gaps are classified:
//   UNCOVERED_ABSENCE  — nobody is rostered there AND at least one of the
//                        location's own (classifying) agents has an absence
//                        ShiftEntry that day (AL/HALF_AL/SL/UL/OL/PH/LPH/
//                        OFF/CD). The "duty left on someone on leave" case
//                        this was built for. Critical.
//   UNCOVERED_ASSIGNED — nobody is rostered there and none of the location's
//                        own agents are absent either — they're all working
//                        elsewhere or on BO. Warning.
//
// A location with active assignments but ZERO cover evidence on any open day
// MORE THAN 7 DAYS out never gets per-day rows. Today's and this week's rows
// are an artifact of how the interactive board works (drag-and-drop happens
// close to the date) — not evidence the location is planned that far out —
// so they don't count toward keeping a location out of this bucket. Such a
// location gets one NOT_RECORDED summary row instead: a centre whose duty is
// simply never entered is a record-keeping problem, not a day-by-day gap
// list, and mixing the two drowns the real gaps. A location with no active
// assignments at all gets one NO_ASSIGNMENT summary row. Both summary rows
// report how many days in the window the location was open.
//
// National public holidays are excluded; the window is capped at the last
// ShiftDate present in ShiftEntries (the generated roster horizon) so dates
// past the end of the roster don't show up as false gaps. Default window is
// 14 days — a gap 8 weeks out isn't actionable — with `days` to reach further.
// Status: the raw ShiftType — drives classification (AbsenceTypes.Contains).
// TaskLabel: what to actually show the user — WHERE they are / what they're
// doing, not just "working" (which only says they have some shift, not
// whether pulling them in would open a hole somewhere else). See
// ComputeTaskLabel for the exact rules.
public record WicGapAgent(string? EmployeeId, string FullName, string AssignmentType, string Status, string TaskLabel);

public record WicCoverageGapEntry(
    string Date, string Weekday, string LocationCode, string DisplayName, string Category,
    string? OpenTime, string? CloseTime, string? OpenTime2, string? CloseTime2,
    List<WicGapAgent> AssignedAgents, List<WicGapAgent> AvailableFloaters);

public record WicCoverageSummaryEntry(
    string LocationCode, string DisplayName, string Reason, int OpenDaysInWindow);

public record WicCoverageGapResult(
    string From, string To,
    List<WicCoverageGapEntry> Gaps, List<WicCoverageSummaryEntry> NotRecorded,
    int CriticalCount, int WarningCount);

public class WicCoverageGapService
{
    private readonly GSDContext _db;
    public WicCoverageGapService(GSDContext db) => _db = db;

    public const int DefaultDays = 14;
    private const int NotRecordedLookaheadDays = 7;

    // Shared with WicAssignCandidatesService so "why is this gap red" and "why is
    // this candidate greyed out" use the exact same absence definition.
    internal static readonly HashSet<string> AbsenceTypes = new(StringComparer.OrdinalIgnoreCase)
        { "AL", "HALF_AL", "SL", "UL", "OL", "PH", "LPH", "OFF", "CD" };

    // Matches ShiftEntry.AgentTask against a WicLocation the same way
    // WicScheduleService.GetClosedDayDutiesAsync resolves orphaned WIC_DUTY
    // rows: exact DisplayName, or the 20-char-truncated prefix
    // WicShiftService.CreateAssignmentAsync writes for long names.
    internal static bool MatchesAgentTask(string agentTask, WicLocation loc) =>
        string.Equals(loc.DisplayName, agentTask, StringComparison.OrdinalIgnoreCase) ||
        (agentTask.Length == 20 && loc.DisplayName.StartsWith(agentTask, StringComparison.OrdinalIgnoreCase));

    // Single source of truth for "is this date covered at this location" — was
    // duplicated byte-for-byte in RosterService.GenerateLocationCoverageAsync,
    // which is exactly the drift Part 2 exists to prevent: a run must never carry
    // its own copy of the definition the coverage-gaps endpoint uses to grade it.
    internal static HashSet<DateOnly> ComputeCoveredDates(
        List<WicShiftEntry> onSiteEntries, List<ShiftEntry> wicDutyEntries, WicLocation loc)
    {
        var set = onSiteEntries
            .Where(w => WicLocationMatcher.MatchesSupportLocation(w.SupportLocation, loc))
            .Select(w => w.ShiftDate)
            .ToHashSet();
        foreach (var d in wicDutyEntries.Where(d => MatchesAgentTask(d.AgentTask!, loc)))
            set.Add(d.ShiftDate);
        return set;
    }

    // Single source of truth for "which dates is this location open" (national
    // holidays excluded) — also previously duplicated in RosterService.
    internal static List<DateOnly> GetOpenDates(
        WicLocation loc, DateOnly from, DateOnly to,
        List<WicOpeningHour> allHours, HashSet<DateOnly> nationalHolidays)
    {
        var openDates = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (nationalHolidays.Contains(date)) continue;
            int dow = (int)date.DayOfWeek;
            var hours = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy, dow, date);
            bool isOpen = hours != null && !hours.IsClosed &&
                !(string.IsNullOrEmpty(hours.OpenTime) && string.IsNullOrEmpty(hours.CloseTime));
            if (isOpen) openDates.Add(date);
        }
        return openDates;
    }

    // What to show the user for a gap-row agent's day: not just "working" (that
    // only says they have SOME shift, not where or doing what) — the actual
    // centre if it's WIC duty elsewhere, BO, VWIC, Training, or the absence
    // code unchanged. Falls back to the raw AgentTask/ShiftType so nothing new
    // is silently swallowed, and only reads "rostered, no task" when the
    // record genuinely carries neither a RawValue nor an AgentTask.
    internal static string ComputeTaskLabel(ShiftEntry? shift)
    {
        if (shift == null) return "not rostered";
        var type = shift.ShiftType ?? "";

        if (AbsenceTypes.Contains(type)) return type; // AL, SL, ... — unchanged
        if (string.Equals(type, "OFF_WEEKEND", StringComparison.OrdinalIgnoreCase)) return "OFF";
        if (string.Equals(type, "RESIGNED", StringComparison.OrdinalIgnoreCase)) return "RESIGNED";

        if (string.Equals(type, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(shift.AgentTask) ? "WIC duty" : $"WIC {shift.AgentTask}";

        if (string.Equals(type, "TRAINING", StringComparison.OrdinalIgnoreCase)) return "Training";

        if (string.Equals(type, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(shift.RawValue, "BO", StringComparison.OrdinalIgnoreCase)) return "BO";
            if (string.Equals(shift.AgentTask, "VWIC", StringComparison.OrdinalIgnoreCase)) return "VWIC";
            if (!string.IsNullOrWhiteSpace(shift.AgentTask)) return shift.AgentTask;
            return "rostered, no task";
        }

        return type; // anything else: the raw ShiftType itself
    }

    private static WicGapAgent ToGapAgent(
        WicAgentAssignment a, DateOnly date,
        Dictionary<string, Employee> empByFullName,
        Dictionary<(string, DateOnly), ShiftEntry> shiftByEmpDate)
    {
        empByFullName.TryGetValue(a.EmployeeName, out var emp);
        ShiftEntry? shift = null;
        if (emp != null) shiftByEmpDate.TryGetValue((emp.EmployeeId, date), out shift);
        string status = shift?.ShiftType ?? "NONE";
        return new WicGapAgent(emp?.EmployeeId, a.EmployeeName, a.AssignmentType, status, ComputeTaskLabel(shift));
    }

    public async Task<WicCoverageGapResult> GetGapsAsync(int? daysParam)
    {
        var days = Math.Clamp(daysParam ?? DefaultDays, 1, 180);
        var from = DateOnly.FromDateTime(DateTime.Today);
        var to   = from.AddDays(days - 1);
        var notRecordedCutoff = from.AddDays(NotRecordedLookaheadDays);

        // Roster horizon cap — dates past the last generated ShiftEntry aren't real
        // gaps, nobody has rostered that far yet.
        var lastShiftDate = await _db.ShiftEntries.MaxAsync(s => (DateOnly?)s.ShiftDate);
        if (lastShiftDate != null && to > lastShiftDate.Value)
            to = lastShiftDate.Value;
        if (to < from)
            return new WicCoverageGapResult(from.ToString("yyyy-MM-dd"), from.ToString("yyyy-MM-dd"), new(), new(), 0, 0);

        var locations = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var allHours  = await _db.WicOpeningHours.ToListAsync();
        var nationalHolidays = new HashSet<DateOnly>(
            await _db.PublicHolidays
                .Where(h => h.IsNational && h.HolidayDate >= from && h.HolidayDate <= to)
                .Select(h => h.HolidayDate)
                .ToListAsync());

        // Cover signal 1: WicShiftEntries.IsOnSite=1, matched by SupportLocation.
        var onSiteEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= from && w.ShiftDate <= to && w.IsOnSite)
            .ToListAsync();
        // Cover signal 2: ShiftEntries WIC_DUTY rows, matched by AgentTask.
        var wicDutyEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to &&
                        s.ShiftType == ShiftTypes.WicDuty && s.AgentTask != null && s.AgentTask != "")
            .ToListAsync();

        var coveredDatesByLocation = locations.ToDictionary(
            l => l.Id, l => ComputeCoveredDates(onSiteEntries, wicDutyEntries, l));

        var allAssignments = await _db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();
        var coreAssignmentsByLocation = locations.ToDictionary(
            l => l.Id,
            l => allAssignments
                .Where(a => WicLocationMatcher.MatchesAssignmentCode(a.LocationCode, l) &&
                            (a.AssignmentType == "MAIN" || a.AssignmentType == "BACKUP"))
                .ToList());
        var regionalAssignmentsByLocation = locations.ToDictionary(
            l => l.Id,
            l => allAssignments
                .Where(a => WicLocationMatcher.MatchesAssignmentCode(a.LocationCode, l) &&
                            a.AssignmentType == "REGIONAL")
                .ToList());
        int regionalOnlyLocationCount = locations.Count(l =>
            coreAssignmentsByLocation[l.Id].Count == 0 && regionalAssignmentsByLocation[l.Id].Count > 0);

        var assignedNames = allAssignments
            .Select(a => a.EmployeeName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var assignedEmployees = await _db.Employees
            .Where(e => e.FullName != null && assignedNames.Contains(e.FullName))
            .ToListAsync();
        var empByFullName = assignedEmployees
            .GroupBy(e => e.FullName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var empIds = assignedEmployees.Select(e => e.EmployeeId).ToList();
        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to && empIds.Contains(s.EmployeeId))
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));

        var gaps = new List<WicCoverageGapEntry>();
        var notRecorded = new List<WicCoverageSummaryEntry>();

        foreach (var loc in locations)
        {
            // Open dates for this location in the window (national holidays excluded).
            var openDates = GetOpenDates(loc, from, to, allHours, nationalHolidays);
            if (openDates.Count == 0) continue;

            var coreAgents     = coreAssignmentsByLocation[loc.Id];
            var regionalAgents = regionalAssignmentsByLocation[loc.Id];
            bool regionalOnly  = coreAgents.Count == 0 && regionalAgents.Count > 0;
            var classifyingAssignments = regionalOnly ? regionalAgents : coreAgents;

            if (classifyingAssignments.Count == 0)
            {
                notRecorded.Add(new WicCoverageSummaryEntry(
                    loc.LocationCode, loc.DisplayName, "NO_ASSIGNMENT", openDates.Count));
                continue;
            }

            var coveredDates = coveredDatesByLocation[loc.Id];
            bool hasFutureCover = coveredDates.Any(d => d > notRecordedCutoff);
            if (!hasFutureCover)
            {
                notRecorded.Add(new WicCoverageSummaryEntry(
                    loc.LocationCode, loc.DisplayName, "NOT_RECORDED", openDates.Count));
                continue;
            }

            foreach (var date in openDates)
            {
                if (coveredDates.Contains(date)) continue; // covered

                var agents = classifyingAssignments
                    .Select(a => ToGapAgent(a, date, empByFullName, shiftByEmpDate))
                    .OrderBy(a => a.FullName)
                    .ToList();

                bool anyAbsent = agents.Any(a => AbsenceTypes.Contains(a.Status));
                string category = anyAbsent ? "UNCOVERED_ABSENCE" : "UNCOVERED_ASSIGNED";

                // Available-floater hint: REGIONAL agents shared with this location who
                // are NOT absent today — never drives the category, only who to grab.
                var floaters = regionalOnly
                    ? new List<WicGapAgent>()
                    : regionalAgents
                        .Select(a => ToGapAgent(a, date, empByFullName, shiftByEmpDate))
                        .Where(a => !AbsenceTypes.Contains(a.Status))
                        .OrderBy(a => a.FullName)
                        .ToList();

                var hours = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy, (int)date.DayOfWeek, date);
                gaps.Add(new WicCoverageGapEntry(
                    date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(),
                    loc.LocationCode, loc.DisplayName, category,
                    hours?.OpenTime, hours?.CloseTime, hours?.OpenTime2, hours?.CloseTime2,
                    agents, floaters));
            }
        }

        // Critical (UNCOVERED_ABSENCE) first, then warning, each chronological.
        gaps = gaps
            .OrderBy(g => g.Category == "UNCOVERED_ABSENCE" ? 0 : 1)
            .ThenBy(g => g.Date).ThenBy(g => g.DisplayName)
            .ToList();
        notRecorded = notRecorded.OrderBy(n => n.DisplayName).ToList();

        int criticalCount = gaps.Count(g => g.Category == "UNCOVERED_ABSENCE");
        int warningCount  = gaps.Count(g => g.Category == "UNCOVERED_ASSIGNED");

        return new WicCoverageGapResult(
            from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"),
            gaps, notRecorded, criticalCount, warningCount);
    }

    /// <summary>
    /// Ground truth for "how many of this location's open days in [from,to] are
    /// still uncovered right now" — built from the exact same ComputeCoveredDates/
    /// GetOpenDates this endpoint uses. RosterService.GenerateLocationCoverageAsync
    /// calls this after it writes, so its own report can never silently drift from
    /// what GET /api/wic/coverage-gaps would say (Part 2: the run must verify
    /// against the endpoint logic, not its own counters).
    /// </summary>
    public async Task<int> GetStillGapCountAsync(string locationCode, DateOnly from, DateOnly to)
    {
        var location = await _db.WicLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode && l.IsActive);
        if (location == null) return 0;

        var allHours = await _db.WicOpeningHours.ToListAsync();
        var nationalHolidays = new HashSet<DateOnly>(
            await _db.PublicHolidays.Where(h => h.IsNational && h.HolidayDate >= from && h.HolidayDate <= to)
                .Select(h => h.HolidayDate).ToListAsync());
        var openDates = GetOpenDates(location, from, to, allHours, nationalHolidays);
        if (openDates.Count == 0) return 0;

        var onSiteEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= from && w.ShiftDate <= to && w.IsOnSite).ToListAsync();
        var wicDutyEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to &&
                        s.ShiftType == ShiftTypes.WicDuty && s.AgentTask != null && s.AgentTask != "")
            .ToListAsync();
        var covered = ComputeCoveredDates(onSiteEntries, wicDutyEntries, location);
        return openDates.Count(d => !covered.Contains(d));
    }
}

public static class WicCoverageGapEndpointMapper
{
    public static void MapWicCoverageGapEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/wic").WithTags("WIC");

        // RTM / TEAM_LEAD / DEV only — same gate as /api/roster/missing
        // (RosterAccess), enforced regardless of Auth:EnforceAuthorization.
        grp.MapGet("/coverage-gaps", async (int? days, HttpContext ctx, WicCoverageGapService svc) =>
        {
            if (RosterAccess.DenyResult(ctx) is { } deny) return deny;
            return Results.Ok(await svc.GetGapsAsync(days));
        });
    }
}
