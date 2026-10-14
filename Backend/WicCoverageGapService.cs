using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.WicShifts;

// ── WIC coverage gaps (GET /api/wic/coverage-gaps) ───────────────────────────
// A day is a gap for an ACTIVE WicLocation when the location is OPEN that day
// (WicHoursResolver, manual exception + EffectiveFrom + legacy-code aware), it
// is not a public holiday, and fewer agents are present than that day's
// MinRequired. Presence is read once, through the shared WicPresenceResolver
// (the same reading the forecast, the cards and the briefing use), from either
// source the rest of the codebase already treats as authoritative:
//   - a WicShiftEntries row for this location+date with IsOnSite=1, or
//   - a ShiftEntries row (any employee) that AvailabilityResolver.IsWicDutyAt
//     reads as WIC duty at this location — its AgentTask resolves to this
//     centre (WicLocationMatcher.MatchesAgentTask: exact DisplayName/code, or
//     the 20-char-truncated prefix the writers persist for long names) and its
//     ShiftType is not an absence/away type.
// A raw "WORKING" ShiftEntry proves nothing either way — it only means the
// agent is clocked in somewhere, not that they're covering this site — so it
// is never treated as cover. A WORKING row whose AgentTask names THIS centre is
// not a raw WORKING row, though: AgentTask only ever holds a centre name on the
// WIC_DUTY encoding, so that row is WIC duty with a flipped ShiftType and does
// count. IsWicDutyAt owns that call for every reader — see its remarks.
// Present/Required are returned on every row so the banner and the Coverage Risk
// list can show the exact same number from this one computation.
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
// A location gets ONE NOT_RECORDED summary row only for open days BEYOND the next
// 14 days when it has no cover evidence at all out there — a centre whose duty is
// simply never entered that far out is a record-keeping problem, not a day-by-day
// gap list. The next 14 days are never summarised this way: an open centre with
// fewer agents than MinRequired is a per-day gap, and the banner and the Coverage
// Risk list both read that same number from this endpoint. A location with no
// active assignments at all gets one NO_ASSIGNMENT summary row for those far days.
// The summary reports how many of those far-out days the location was open.
//
// National and regional public holidays are excluded; the window is capped at the
// last ShiftDate present in ShiftEntries (the generated roster horizon) so dates
// past the end of the roster don't show up as false gaps. Default window is
// 14 days — a gap 8 weeks out isn't actionable — with `days` to reach further
// (the Overview 90-day view asks for days=90; the endpoint clamps at 180).
// Status: the raw ShiftType — drives classification (AbsenceTypes.Contains).
// TaskLabel: what to actually show the user — WHERE they are / what they're
// doing, not just "working" (which only says they have some shift, not
// whether pulling them in would open a hole somewhere else). See
// ComputeTaskLabel for the exact rules.
public record WicGapAgent(string? EmployeeId, string FullName, string AssignmentType, string Status, string TaskLabel);

public record WicCoverageGapEntry(
    string Date, string Weekday, string LocationCode, string DisplayName, string City, string Category,
    int Present, int Required,
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

    // "Record-keeping" may only describe open days BEYOND this horizon. A day inside the
    // next 14 days is always judged as a staffing gap: an open centre with fewer agents
    // than MinRequired is a gap now, and the record-keeping bucket must never silence it.
    // 2026-10-13 Demmin - Woldeforster Str and Wesel were open and uncovered on every open
    // day of the window, yet the whole location was summarised as NOT_RECORDED (because its
    // only cover evidence sat inside the old 7-day cutoff) and the banner stayed green while
    // the Coverage Risk list showed the same locations as UNCOVERED.
    internal const int RecordKeepingLookaheadDays = 14;

    // Shared with WicAssignCandidatesService so "why is this gap red" and "why is
    // this candidate greyed out" use the exact same absence definition.
    internal static readonly HashSet<string> AbsenceTypes = new(StringComparer.OrdinalIgnoreCase)
        { "AL", "HALF_AL", "SL", "UL", "OL", "PH", "LPH", "OFF", "CD" };

    // Candidate cover rows are pre-filtered in SQL on this list: a task badge (or
    // "GSD") can never be a centre name, so those rows cannot be cover and are not
    // worth loading. AvailabilityResolver.IsWicDutyAt makes the real decision.
    private static readonly List<string> NonLocationTasks = AgentTasks.NonLocationValues;

    // Single source of truth for "is this date covered at this location" — was
    // duplicated byte-for-byte in RosterService.GenerateLocationCoverageAsync,
    // which is exactly the drift Part 2 exists to prevent: a run must never carry
    // its own copy of the definition the coverage-gaps endpoint uses to grade it.
    //
    // ShiftEntries is authoritative over WicShiftEntries (documented known artefact,
    // technical.md §"Known artefacts": a later import can set ShiftEntries to an
    // absence without ever clearing the paired WicShiftEntries.IsOnSite flag — e.g.
    // a vacation import sets AL but leaves a stale IsOnSite=1 row behind). An on-site
    // row therefore does not count as coverage when the same EmployeeId+ShiftDate has
    // a ShiftEntries full absence (AvailabilityResolver.FullAbsenceTypes) — the same
    // rule every AvailabilityResolver.GetWicContribution caller already applies.
    // onSiteEmployeeShiftEntries only needs to cover onSiteEntries' employees/dates;
    // filtering to FullAbsenceTypes happens in here so callers don't have to remember to.
    internal static HashSet<DateOnly> ComputeCoveredDates(
        List<WicShiftEntry> onSiteEntries, List<ShiftEntry> wicDutyEntries, WicLocation loc,
        List<ShiftEntry> onSiteEmployeeShiftEntries)
    {
        // Delegates to WicPresenceResolver so this endpoint reads coverage exactly the way
        // the forecast, the briefing and the location cards do. Before, this was its own
        // fourth reading of the same question.
        //
        // isSick is `_ => false` on purpose: this overload has no SickLeaves data. A sick
        // day normally also lands in ShiftEntries as ShiftType=SL (ShiftSyncService), which
        // the resolver already scores 0.0, so the absence is still caught.
        var shiftByEmpDate = onSiteEmployeeShiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));

        // Only dates that some row actually mentions can be covered, so there is no need
        // for the caller to pass a range.
        var candidateDates = onSiteEntries.Select(w => w.ShiftDate)
            .Concat(wicDutyEntries.Select(d => d.ShiftDate))
            .ToHashSet();

        var covered = new HashSet<DateOnly>();
        foreach (var date in candidateDates)
        {
            var presence = WicPresenceResolver.Resolve(
                loc, date, onSiteEntries, shiftByEmpDate, wicDutyEntries, _ => false);
            // "Is anybody rostered here at all" — this endpoint classifies assignment
            // structure, so any contribution counts. MinRequired is the forecast's job.
            if (presence.PresentDouble > 0) covered.Add(date);
        }
        return covered;
    }

    // Shared query so every ComputeCoveredDates caller builds the absence cross-check
    // the same way: all ShiftEntries rows for the employees who have an on-site
    // WicShiftEntries row in range (FullAbsenceTypes filtering happens inside
    // ComputeCoveredDates itself).
    internal static async Task<List<ShiftEntry>> GetShiftEntriesForOnSiteEmployeesAsync(
        GSDContext db, List<WicShiftEntry> onSiteEntries, DateOnly from, DateOnly to)
    {
        var empIds = onSiteEntries.Select(w => w.EmployeeId).Distinct().ToList();
        if (empIds.Count == 0) return new List<ShiftEntry>();
        return await db.ShiftEntries
            .Where(s => empIds.Contains(s.EmployeeId) && s.ShiftDate >= from && s.ShiftDate <= to)
            .ToListAsync();
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
            // A centre name here means WIC duty with a flipped ShiftType
            // (AvailabilityResolver.IsWicDutyAt) — show it as the WIC duty it is, so the
            // row reads the same as a proper WIC_DUTY row instead of as a bare task.
            if (!string.IsNullOrWhiteSpace(shift.AgentTask))
                return AgentTasks.IsNonLocationValue(shift.AgentTask)
                    ? shift.AgentTask
                    : $"WIC {shift.AgentTask}";
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
        // Everything after this date may be summarised as NOT_RECORDED when a location has
        // no plan at all out there. The next 14 days are never record-keeping.
        var recordKeepingCutoff = from.AddDays(RecordKeepingLookaheadDays - 1);

        // Roster horizon cap — dates past the last generated ShiftEntry aren't real
        // gaps, nobody has rostered that far yet.
        var lastShiftDate = await _db.ShiftEntries.MaxAsync(s => (DateOnly?)s.ShiftDate);
        if (lastShiftDate != null && to > lastShiftDate.Value)
            to = lastShiftDate.Value;
        if (to < from)
            return new WicCoverageGapResult(from.ToString("yyyy-MM-dd"), from.ToString("yyyy-MM-dd"), new(), new(), 0, 0);

        var locations = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var allHours  = await _db.WicOpeningHours.ToListAsync();
        var holidays  = await _db.PublicHolidays
            .Where(h => h.HolidayDate >= from && h.HolidayDate <= to)
            .ToListAsync();
        var nationalHolidays = new HashSet<DateOnly>(
            holidays.Where(h => h.IsNational).Select(h => h.HolidayDate));

        // Cover signals — the exact inputs WicPresenceResolver reads, bulk-loaded once.
        // On-site rows are restricted to active employees, the same join ForecastService
        // makes, so "who is present" cannot differ between the forecast and this list.
        var onSiteEntries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate >= from && w.ShiftDate <= to && w.IsOnSite)
            .Join(_db.Employees.Where(e => e.IsActive),
                  w => w.EmployeeId, e => e.EmployeeId, (w, _) => w)
            .ToListAsync();
        // Cover signal 2: ShiftEntries rows whose AgentTask could name a centre. The
        // ShiftType call belongs to AvailabilityResolver.IsWicDutyAt, not to this query —
        // filtering on ShiftType=WIC_DUTY here is what used to drop a WIC-duty row whose
        // ShiftType had been flipped to WORKING, producing a phantom gap.
        var wicDutyEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to &&
                        s.AgentTask != null && s.AgentTask != "" &&
                        !NonLocationTasks.Contains(s.AgentTask))
            .ToListAsync();
        // Every ShiftEntries row in range: the resolver scores an on-site agent from their
        // own (employee, date) row, so the dictionary must not be limited to assigned agents.
        var shiftEntries = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to)
            .ToListAsync();
        var shiftByEmpDate = shiftEntries
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));

        // Who has already left by a date in this window. Loaded once for the whole window
        // (the earliest RESIGNED row per employee answers every date). IsActive alone is not
        // enough: the live Hannover case (9130648, 2026-11-02) still had IsActive=1.
        var resignedRows = await _db.ShiftEntries
            .Where(s => s.ShiftType == SubstituteEligibility.Resigned && s.ShiftDate <= to)
            .ToListAsync();
        var resignedFrom = SubstituteEligibility.EarliestResignedByEmployee(resignedRows);
        var sickLeaves = await _db.SickLeaves
            .Where(sl => sl.EmployeeId != null && sl.FirstDay <= to && sl.LastDay >= from)
            .ToListAsync();

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

        var assignedNames = allAssignments
            .Select(a => a.EmployeeName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var assignedEmployees = await _db.Employees
            .Where(e => e.FullName != null && assignedNames.Contains(e.FullName)
                     && !TestAccounts.ExcludedEmployeeIds.Contains(e.EmployeeId))
            .ToListAsync();
        var empByFullName = assignedEmployees
            .GroupBy(e => e.FullName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var empById = assignedEmployees
            .GroupBy(e => e.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Never offer an agent who has left: inactive Employees row, or a RESIGNED
        // ShiftEntries row on/before the day (SubstituteEligibility). AbsenceTypes already
        // keeps AL/SL/UL/OL/PH/LPH/OFF/CD floaters out of this list; RESIGNED is not an
        // absence, so it needs the explicit check.
        bool HasLeft(string? employeeId, DateOnly date) =>
            (employeeId != null && empById.TryGetValue(employeeId, out var e) && !e.IsActive) ||
            SubstituteEligibility.ResignedOnOrBefore(employeeId, date, resignedFrom);

        // ONE presence reading per (location, date), through the same resolver as the
        // forecast — never a second definition of "covered" living only in this file.
        var presenceCache = new Dictionary<(int, DateOnly), double>();
        double PresenceFor(WicLocation loc, DateOnly date)
        {
            if (presenceCache.TryGetValue((loc.Id, date), out var cached)) return cached;
            var presence = WicPresenceResolver.Resolve(
                loc, date, onSiteEntries, shiftByEmpDate, wicDutyEntries,
                empId => sickLeaves.Any(sl =>
                    sl.EmployeeId == empId && sl.FirstDay <= date && sl.LastDay >= date));
            presenceCache[(loc.Id, date)] = presence.PresentDouble;
            return presence.PresentDouble;
        }

        WicOpeningHour? HoursFor(WicLocation loc, DateOnly date) =>
            WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy, (int)date.DayOfWeek, date);

        int MinRequiredFor(WicLocation loc, WicOpeningHour? hours) =>
            hours?.MinRequired ?? loc.MinAgentsRequired ?? 1;

        // Same "is the centre open that day" answer every other surface gets: the manual
        // exception row wins, then the weekday pattern, then national/regional holidays.
        bool IsOpenOn(WicLocation loc, DateOnly date)
        {
            var hours = HoursFor(loc, date);
            if (hours == null || hours.IsClosed) return false;
            if (nationalHolidays.Contains(date)) return false;
            var bundesland = loc.Bundesland ?? PlzBundesland.Get(loc.LocationCode, loc.PostalCode, loc.Country);
            return bundesland == null || !holidays.Any(h =>
                h.HolidayDate == date && string.Equals(h.Bundesland, bundesland, StringComparison.OrdinalIgnoreCase));
        }

        WicCoverageGapEntry BuildGap(
            WicLocation loc, DateOnly date,
            List<WicAgentAssignment> classifying, List<WicAgentAssignment> regional, bool regionalOnly)
        {
            var hours = HoursFor(loc, date);
            var agents = classifying
                .Select(a => ToGapAgent(a, date, empByFullName, shiftByEmpDate))
                .OrderBy(a => a.FullName)
                .ToList();
            bool anyAbsent = agents.Any(a => AbsenceTypes.Contains(a.Status));
            string category = anyAbsent ? "UNCOVERED_ABSENCE" : "UNCOVERED_ASSIGNED";

            // Available-floater hint: REGIONAL agents shared with this location who are NOT
            // absent today and have NOT left the company by this day — never drives the
            // category, only who to grab.
            var floaters = regionalOnly
                ? new List<WicGapAgent>()
                : regional
                    .Select(a => ToGapAgent(a, date, empByFullName, shiftByEmpDate))
                    .Where(a => !AbsenceTypes.Contains(a.Status))
                    .Where(a => !HasLeft(a.EmployeeId, date))
                    .OrderBy(a => a.FullName)
                    .ToList();

            return new WicCoverageGapEntry(
                date.ToString("yyyy-MM-dd"), date.DayOfWeek.ToString(),
                loc.LocationCode, loc.DisplayName, loc.City ?? "", category,
                (int)Math.Floor(PresenceFor(loc, date)), MinRequiredFor(loc, hours),
                hours?.OpenTime, hours?.CloseTime, hours?.OpenTime2, hours?.CloseTime2,
                agents, floaters);
        }

        var gaps = new List<WicCoverageGapEntry>();
        var notRecorded = new List<WicCoverageSummaryEntry>();

        foreach (var loc in locations)
        {
            var openDates = new List<DateOnly>();
            for (var date = from; date <= to; date = date.AddDays(1))
                if (IsOpenOn(loc, date)) openDates.Add(date);
            if (openDates.Count == 0) continue;

            var coreAgents     = coreAssignmentsByLocation[loc.Id];
            var regionalAgents = regionalAssignmentsByLocation[loc.Id];
            bool regionalOnly  = coreAgents.Count == 0 && regionalAgents.Count > 0;
            var classifying    = regionalOnly ? regionalAgents : coreAgents;

            // The next 14 days are judged per day. An open centre short of MinRequired is a
            // gap here — never a record-keeping summary, whatever its other cover looks like.
            foreach (var date in openDates.Where(d => d <= recordKeepingCutoff))
            {
                if (PresenceFor(loc, date) >= MinRequiredFor(loc, HoursFor(loc, date))) continue;
                gaps.Add(BuildGap(loc, date, classifying, regionalAgents, regionalOnly));
            }

            var farDates = openDates.Where(d => d > recordKeepingCutoff).ToList();
            if (farDates.Count == 0) continue;

            if (classifying.Count == 0)
            {
                notRecorded.Add(new WicCoverageSummaryEntry(
                    loc.LocationCode, loc.DisplayName, "NO_ASSIGNMENT", farDates.Count));
                continue;
            }

            // Beyond the actionable horizon, a location with no plan at all is a
            // record-keeping problem, not a day-by-day gap list.
            if (!farDates.Any(d => PresenceFor(loc, d) > 0))
            {
                notRecorded.Add(new WicCoverageSummaryEntry(
                    loc.LocationCode, loc.DisplayName, "NOT_RECORDED", farDates.Count));
                continue;
            }

            foreach (var date in farDates)
            {
                if (PresenceFor(loc, date) >= MinRequiredFor(loc, HoursFor(loc, date))) continue;
                gaps.Add(BuildGap(loc, date, classifying, regionalAgents, regionalOnly));
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
                        s.AgentTask != null && s.AgentTask != "" &&
                        !NonLocationTasks.Contains(s.AgentTask))
            .ToListAsync();
        var onSiteEmployeeShiftEntries = await GetShiftEntriesForOnSiteEmployeesAsync(_db, onSiteEntries, from, to);
        var covered = ComputeCoveredDates(onSiteEntries, wicDutyEntries, location, onSiteEmployeeShiftEntries);
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
