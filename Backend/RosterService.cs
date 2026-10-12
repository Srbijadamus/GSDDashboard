using System.Globalization;
using System.Text.Json;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.Shifts;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Roster;

// ---------------------------------------------------------------------------
// Roster creation for RTM: pick employee + date range + times + working days,
// generate WORKING ShiftEntries rows. Weekends (Sat/Sun) are always skipped —
// the same rule ShiftSyncService uses — and public holidays are skipped the way
// PublicHolidayService.GetAgentHolidaysAsync resolves them (national plus the
// employee's own Bundesland). Every generated row carries SourceModule="Roster"
// and SourceId=<batch id> so a batch can be found and deleted again.
//
// WIC mode (LocationCode set): RTM picks only agent + WIC location + range.
// Days the location is OPEN per WicOpeningHours (WicHoursResolver, EffectiveFrom-
// and legacy-code aware) become WIC duty; every other weekday becomes BO.
// WorkingDays/AgentTask from the request are ignored in this mode.
//   WIC rows match the shape WicShiftService.CreateAssignmentAsync writes
//   (ShiftType=WIC_DUTY, IsWicDuty=true, AgentTask=location DisplayName, times =
//   the location's opening hours for that date, plus the WicShiftEntry upsert
//   with IsOnSite=true / Task="WIC") — only SourceModule/SourceId differ so the
//   batch stays deletable. BO rows match what BO looks like in ShiftEntries
//   today (ShiftType=WORKING, RawValue="BO"). The generator NEVER silently
//   writes a permanent MAIN WicAgentAssignments row (see ResolveMainAsync):
//     * an active MAIN at another centre is left untouched and reported as
//       MAIN_UNCHANGED — a roster run is not a transfer;
//     * an agent with no MAIN at all gets one only when the caller explicitly
//       confirmed it (confirmCreateMain=true, sent by the UI confirmation
//       dialog "This will set <centre> as the agent's permanent main WIC").
//
// Collision rule: by default a date that already has ANY ShiftEntry for the
// employee is skipped and reported; existing rows are never modified. With
// Overwrite=true the caller opts into replacement: WORKING, BO, WIC_DUTY and
// EMPTY rows are replaced by the generated row, while absence rows are NEVER
// touched (ProtectedAbsenceTypes — the same blocking list the WIC assignment
// endpoint WicShiftService.CreateAssignmentAsync uses, plus CD). Every replaced
// row is snapshotted to RosterBatches.ReplacedRowsJson first, so deleting the
// batch restores what was there before (unless another module has since
// claimed that employee+date — then the restore of that date is skipped and
// reported, never overwritten).
// ---------------------------------------------------------------------------

public class RosterValidationException : Exception
{
    public RosterValidationException(string message) : base(message) { }
}

public record RosterRequest(
    string? EmployeeId, string? DateFrom, string? DateTo,
    string? ShiftStart, string? ShiftEnd, int[]? WorkingDays, string? AgentTask,
    string? LocationCode = null, bool Overwrite = false,
    // Explicit opt-in for writing a permanent MAIN WIC assignment when the agent
    // has none yet. Never implied by LocationCode alone — see ResolveMainAsync.
    bool ConfirmCreateMain = false);

public record RosterSkippedHoliday(string Date, string Name);
// Protected=true means IsProtectedFromOverwrite refused the row (an absence,
// training/VWIC, WIC duty elsewhere, or a manual change) — enabling Overwrite
// would NOT replace it. Protected=false means it was skipped only because
// Overwrite was off, so the caller can say which flag would take care of it.
public record RosterCollision(string Date, string ShiftType, string? SourceModule, bool Protected = false);

// One existing row that Overwrite mode would replace: which date, what is
// there now, and what the generated row would look like.
public record RosterReplacement(
    string Date, string ExistingShiftType, string? ExistingSourceModule,
    string Kind, string ShiftStart, string ShiftEnd);

// Kind: "WORKING" (plain roster), "WIC" (location open that day -> WIC duty),
// "BO" (location closed that weekday -> back office).
public record RosterPlanRow(string Date, string ShiftStart, string ShiftEnd, string Kind);

public record RosterPreviewResult(
    string EmployeeId, string? FullName,
    int RowCount, string? FirstDate, string? LastDate,
    int SkippedWeekends,
    List<RosterSkippedHoliday> SkippedHolidays,
    List<RosterCollision> Collisions,
    List<RosterPlanRow> Rows,
    int WicDays, int BoDays,
    string? LocationCode, string? LocationName,
    int ReplaceCount, List<RosterReplacement> Replacements,
    // ── Permanent MAIN WIC decision of this call ────────────────────────────
    // ExistingMainCode/Name: the agent's active MAIN row right now (null = none).
    // WouldCreateMain: a WIC location is selected and the agent has NO active
    // MAIN, so confirming would set this centre as the permanent main WIC. The
    // UI shows its confirmation dialog exactly when this is true.
    // MainWarning: the same text the generate call would return (MAIN_UNCHANGED
    // when the agent already has a MAIN elsewhere, MAIN_NOT_CREATED when one
    // would have to be confirmed), so the RTM sees it before generating.
    string? ExistingMainCode = null, string? ExistingMainName = null,
    bool WouldCreateMain = false, string? MainWarning = null);

public record RosterGenerateResult(
    int BatchId, string EmployeeId, int Created, int SkippedExisting,
    string? FirstDate, string? LastDate,
    int WicDays, int BoDays, bool AssignmentCreated, int Replaced,
    // Permanent MAIN WIC outcome of this call:
    //   already_main — this centre already is the agent's MAIN (nothing done)
    //   unchanged    — the agent kept a MAIN at ANOTHER centre (never replaced)
    //   created      — no MAIN existed and confirmCreateMain=true wrote one
    //   not_created  — no MAIN existed and the confirmation was missing
    //   null         — plain roster, or the employee has no FullName to match on
    // MainWarning is the API-level (English) text, e.g.
    // "MAIN_UNCHANGED: agent already has main centre Centre A".
    string? MainAction = null, string? MainWarning = null,
    string? MainCentreName = null);

public record RosterBatchDto(
    int Id, string EmployeeId, string? FullName,
    string DateFrom, string DateTo, string ShiftStart, string ShiftEnd,
    string WorkingDays, string? AgentTask,
    string? LocationCode, string? LocationName,
    int RowCount, int SkippedExisting, int SkippedHolidays,
    int ReplacedExisting, bool HasSnapshot,
    // Shown in the batch table as "Created". Stored UTC, exposed as Berlin wall clock
    // (BerlinTime) so the text is the same for every viewer, not the browser's zone.
    string? CreatedByKid, string? CreatedByName, string CreatedAt);

public record RosterDeleteResult(
    int BatchId, int DeletedRows, int DetachedRows, int WicRowsRemoved,
    int RestoredRows, int RestoredWicRows, int RestoreSkipped);

// Active WIC location for the roster dropdown. OpeningDays/OpeningLabel are
// resolved from WicOpeningHours (WicHoursResolver) — never typed by the user.
public record RosterLocationDto(
    string LocationCode, string DisplayName, string? City, string? Country,
    int[] OpeningDays, string OpeningLabel);

// ── Missing-roster check (GET /api/roster/missing) ──────────────────────────
// Surfaces data gaps nobody is notified about today:
//   MissingRoster  — active employees with ZERO ShiftEntries in the window
//                    (the Gaber/Halim/Henschel case: found only via team-lead mail).
//   OrphanEntries  — ShiftEntries rows in the window whose EmployeeId has no
//                    ACTIVE Employees row (either no row at all, or inactive).
public record MissingRosterAgent(
    string EmployeeId, string? FullName, string? TeamLeadName, string? PrimaryRole,
    string? LastShiftDate);

public record OrphanShiftEntryGroup(
    string EmployeeId, int RowCount, string FirstDate, string LastDate,
    bool HasInactiveEmployeeRow);

// Per active employee, the individual dates within the window that have NO
// ShiftEntries row at all — catches partial/sparse coverage (e.g. weekday rows
// stop after some date but the employee isn't fully "missing") that
// MissingRoster's all-or-nothing zero-rows check cannot see. Only employees
// with at least one gap date are included.
public record EmployeeDateGaps(
    string EmployeeId, string? FullName, List<string> MissingDates);

public record MissingRosterResult(
    int Days, string From, string To,
    List<MissingRosterAgent> MissingRoster,
    List<OrphanShiftEntryGroup> OrphanEntries,
    List<EmployeeDateGaps> DateGapsByEmployee);

/// <summary>
/// Server-side role gate for /api/roster/*: RTM, TEAM_LEAD and DEV only.
/// Always enforced — deliberately NOT behind Auth:EnforceAuthorization, so an
/// AGENT session gets 403 even while the global rollout flag is still off.
/// </summary>
public static class RosterAccess
{
    public static bool IsAllowedRole(string? role) =>
        role is AppRoles.Rtm or AppRoles.TeamLead or AppRoles.Dev;

    /// <summary>Returns the error result to short-circuit with, or null when allowed.</summary>
    public static IResult? DenyResult(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
            return Results.Json(new { error = "Authentication required" },
                                statusCode: StatusCodes.Status401Unauthorized);
        if (!IsAllowedRole(ctx.User.FindFirst(AuthClaims.Role)?.Value))
            return Results.Json(new { error = "Forbidden" },
                                statusCode: StatusCodes.Status403Forbidden);
        return null;
    }
}

public class RosterService
{
    public const string SourceModuleName = "Roster";
    public const string SourceSheetName  = "ROSTER";

    /// <summary>Hard cap on the selectable range so a fat-fingered year range
    /// cannot flood ShiftEntries (same idea as the 90-day cap in ShiftSyncService).</summary>
    public const int MaxRangeDays = 366;

    private readonly GSDContext _db;
    private readonly WicShiftService _wicShiftService;
    private readonly WicCoverageGapService _gapService;
    public RosterService(GSDContext db, WicShiftService wicShiftService, WicCoverageGapService gapService)
    {
        _db = db;
        _wicShiftService = wicShiftService;
        _gapService = gapService;
    }

    // Start/End are nullable: WIC mode (Location != null) normally leaves them
    // null so BuildPlanAsync's per-day centre hours drive the actual times —
    // required only in plain mode, or as an explicit WIC-mode override.
    private sealed record PlanInput(Employee Emp, DateOnly From, DateOnly To,
        string? Start, string? End, HashSet<int> WorkingDays, string? AgentTask,
        WicLocation? Location);

    private sealed record PlanRow(DateOnly Date, string Kind, string Start, string End);

    // Overwrite mode: the generated row plus the existing ShiftEntry it replaces.
    private sealed record ReplacePlan(PlanRow Row, int ExistingId,
        string ExistingType, string? ExistingSourceModule);

    private sealed record Plan(List<PlanRow> ToCreate, List<ReplacePlan> ToReplace,
        int SkippedWeekends,
        List<RosterSkippedHoliday> Holidays, List<RosterCollision> Collisions);

    // ── Permanent MAIN WIC assignment decision ──────────────────────────────
    // The generator must never silently make a centre an agent's permanent home
    // (RosterService.cs:687-709 used to do exactly that whenever the agent had no
    // active assignment for the generated centre).
    private enum MainOutcome
    {
        /// <summary>Plain roster (no WIC location) or no FullName to match on.</summary>
        NoLocation,
        /// <summary>This centre already is the agent's active MAIN — nothing to do.</summary>
        AlreadyMain,
        /// <summary>The agent is MAIN at a different centre — never touched or duplicated.</summary>
        ExistingOtherMain,
        /// <summary>No MAIN existed and confirmCreateMain=true — write it.</summary>
        Created,
        /// <summary>No MAIN existed and the confirmation was missing — write nothing.</summary>
        NotCreated,
    }

    private sealed record MainDecision(MainOutcome Outcome, string? ExistingMainCode, string? ExistingMainName)
    {
        /// <summary>True when this call is about to set (or would set, if confirmed) a MAIN row.</summary>
        public bool WouldCreateMain => Outcome is MainOutcome.Created or MainOutcome.NotCreated;
    }

    /// <summary>
    /// Absence types the generator never overwrites, even with Overwrite=true.
    /// Same blocking list the WIC assignment endpoint uses
    /// (WicShiftService.CreateAssignmentAsync), plus CD (comp day) which the
    /// change request explicitly names protected.
    /// </summary>
    public static readonly HashSet<string> ProtectedAbsenceTypes =
        new(StringComparer.OrdinalIgnoreCase)
        { "AL", "HALF_AL", "SL", "UL", "PH", "LPH", "OFF", "OFF_WEEKEND", "OL",
          "RESIGNED", "CD" };

    /// <summary>
    /// WIC-priority rule (permanent): an existing row the generator must never
    /// replace even with Overwrite=true, beyond ProtectedAbsenceTypes —
    ///   - WIC duty at a DIFFERENT centre than the one being generated (never
    ///     overwrite a WIC commitment elsewhere just because this generate call
    ///     is for a different location or plain mode);
    ///   - TRAINING, and a WORKING row explicitly tasked VWIC — "training or
    ///     VWIC if explicitly scheduled" outranks BO/GSD and must survive a
    ///     regenerate the same way an absence does.
    /// AgentTask is matched via WicLocationMatcher.MatchesAgentTask — the shared
    /// definition, so this guard can never drift from what the coverage readers
    /// consider a WIC commitment.
    /// </summary>
    private static bool IsProtectedFromOverwrite(string existingShiftType, string? existingAgentTask, WicLocation? targetLocation,
        bool isManualChange = false)
    {
        // A manual change is a legitimate decision, never an error and never a gap: the
        // generator must never overwrite or delete it, whatever its ShiftType. This is the
        // documented protection list (FUNCTIONS.md) extended — ProtectedAbsenceTypes alone
        // did not cover a hand-edited WORKING/BO row.
        if (isManualChange) return true;
        if (ProtectedAbsenceTypes.Contains(existingShiftType)) return true;
        if (string.Equals(existingShiftType, ShiftTypes.Training, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(existingShiftType, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existingAgentTask, "VWIC", StringComparison.OrdinalIgnoreCase))
            return true;
        // A WIC commitment is either a WIC_DUTY row or the same row shape with its
        // ShiftType left/flipped to WORKING — AgentTask holding something that is not a
        // task badge (nor "GSD") means it holds a centre name, which is the WIC_DUTY
        // encoding (AvailabilityResolver.IsWicDutyAt). Both must survive a regenerate
        // aimed at ANOTHER centre; this is the guard that would have stopped the
        // overwrite run behind the flipped 2026-10-13 row from touching it at all.
        // Erring toward protection is deliberate: this decides whether a generator may
        // overwrite roster data a human may have entered.
        bool isWicDutyRow  = string.Equals(existingShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase);
        bool isWicShapedRow = string.Equals(existingShiftType, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase) &&
                              !string.IsNullOrWhiteSpace(existingAgentTask) &&
                              !AgentTasks.IsNonLocationValue(existingAgentTask);
        if (isWicDutyRow || isWicShapedRow)
        {
            if (targetLocation == null || string.IsNullOrWhiteSpace(existingAgentTask)) return true;
            // Same centre = this run owns the row, so it may rewrite it (which is also
            // how a flipped ShiftType gets repaired: regenerate that location).
            return !WicLocationMatcher.MatchesAgentTask(existingAgentTask, targetLocation);
        }
        return false;
    }

    // Snapshot of a replaced ShiftEntry (plus its WicShiftEntries rows when it
    // was a WIC_DUTY) so DeleteBatchAsync can restore the pre-batch state.
    // ShiftDate is stored as "yyyy-MM-dd" text to keep the JSON boring.
    private sealed record ShiftEntrySnapshot(
        string EmployeeId, string ShiftDate, string? RawValue, string ShiftType,
        string? ShiftStart, string? ShiftEnd, bool IsWicDuty, string? SourceSheet,
        string? AgentTask, string? LocationId, string? AssignmentStatus,
        bool AutoGenerated, string? SourceModule, int? SourceId,
        string? PreviousStatus, List<WicEntrySnapshot> WicEntries);

    private sealed record WicEntrySnapshot(
        string? DayOfWeek, string? SupportLocation, string? WicOpeningHours,
        string? WorkingShift, bool IsOnSite, bool IsGSDDay, bool IsOffDay,
        string? Task, string? LocationCode);

    // -- validation ----------------------------------------------------------

    private async Task<PlanInput> ValidateAsync(RosterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmployeeId))
            throw new RosterValidationException("employeeId is required");

        var emp = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId.Trim() && e.IsActive);
        if (emp == null)
            throw new RosterValidationException("Unknown or inactive employee: " + req.EmployeeId.Trim());

        if (!DateOnly.TryParse(req.DateFrom, out var from) ||
            !DateOnly.TryParse(req.DateTo,   out var to))
            throw new RosterValidationException("dateFrom and dateTo must be valid dates (yyyy-MM-dd)");
        if (to < from)
            throw new RosterValidationException("dateTo must not be before dateFrom");
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
            throw new RosterValidationException($"Date range must not exceed {MaxRangeDays} days");

        var days = (req.WorkingDays == null || req.WorkingDays.Length == 0)
            ? new HashSet<int> { 1, 2, 3, 4, 5 }
            : req.WorkingDays.ToHashSet();
        if (days.Any(d => d < 0 || d > 6))
            throw new RosterValidationException("workingDays entries must be .NET DayOfWeek numbers 0-6");

        var task = string.IsNullOrWhiteSpace(req.AgentTask) ? null : req.AgentTask.Trim();
        if (task != null && task.Length > 20)
            throw new RosterValidationException("agentTask must not exceed 20 characters");

        // WIC mode: the location must be an active WicLocations row. Opening days
        // are resolved later from WicOpeningHours — never taken from the request.
        WicLocation? location = null;
        if (!string.IsNullOrWhiteSpace(req.LocationCode))
        {
            var code = req.LocationCode.Trim();
            location = await _db.WicLocations
                .FirstOrDefaultAsync(l => l.LocationCode == code && l.IsActive);
            if (location == null)
                throw new RosterValidationException("Unknown or inactive WIC location: " + code);
        }
        else if (emp.FullName != null)
        {
            // WIC priority (permanent rule): an agent who is MAIN for an active WIC
            // location is scheduled there on that location's open days — GSD/BO is
            // the fallback for a WIC agent, never the default, regardless of
            // whether the caller remembered to pass locationCode. Auto-detected
            // only when the caller left LocationCode blank; an explicit
            // LocationCode (e.g. a deliberate REGIONAL/BACKUP support day) is
            // always honoured as given.
            var mainAssignment = await _db.WicAgentAssignments
                .FirstOrDefaultAsync(a => a.IsActive && a.AssignmentType == "MAIN" && a.EmployeeName == emp.FullName);
            if (mainAssignment != null)
            {
                var code = mainAssignment.LocationCode;
                location = await _db.WicLocations
                    .FirstOrDefaultAsync(l => l.IsActive && (l.LocationCode == code || l.LocationCodeLegacy == code));
            }
        }

        // Plain mode always requires an explicit shift time. WIC mode (location
        // resolved, explicit or auto-detected) leaves start/end null unless the
        // caller explicitly overrides both — BuildPlanAsync then prioritises the
        // centre's OWN per-day opening hours over any fallback, so a location
        // with different hours on different weekdays (e.g. Fürstenwalde: Mon/Wed
        // 08:00-15:00, Tue 08:00-12:00, Thu 08:30-12:00) gets each day's actual
        // hours instead of one fixed time smeared across every day.
        string? start = null, end = null;
        bool hasExplicitTime = !string.IsNullOrWhiteSpace(req.ShiftStart) || !string.IsNullOrWhiteSpace(req.ShiftEnd);
        if (location == null || hasExplicitTime)
        {
            start = NormalizeTime(req.ShiftStart, "shiftStart");
            end   = NormalizeTime(req.ShiftEnd,   "shiftEnd");
        }

        return new PlanInput(emp, from, to, start, end, days, task, location);
    }

    private static string NormalizeTime(string? v, string field)
    {
        if (string.IsNullOrWhiteSpace(v) ||
            !TimeOnly.TryParse(v.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            throw new RosterValidationException($"Invalid {field} (expected HH:mm)");
        return t.ToString("HH:mm");
    }

    // -- permanent MAIN WIC decision -----------------------------------------

    /// <summary>
    /// Read-only decision about the agent's permanent MAIN WIC row for this call.
    /// Shared by preview and generate so the rule (and the warning the RTM sees)
    /// can never differ between the two. The caller performs the write.
    /// </summary>
    private async Task<MainDecision> ResolveMainAsync(WicLocation? location, string? employeeName,
        bool confirmCreateMain)
    {
        if (location == null || string.IsNullOrWhiteSpace(employeeName))
            return new MainDecision(MainOutcome.NoLocation, null, null);

        var mains = await _db.WicAgentAssignments
            .Where(a => a.IsActive && a.AssignmentType == "MAIN" && a.EmployeeName == employeeName)
            // OrderBy is not cosmetic: SQL without ORDER BY has no guaranteed row
            // order, so an agent with several MAIN rows (live data has 7) must
            // always be reported with the same one.
            .OrderBy(a => a.Id)
            .ToListAsync();

        // MAIN already names the centre being generated (tilde code or legacy code).
        if (mains.Any(a => WicLocationMatcher.MatchesAssignmentCode(a.LocationCode, location)))
            return new MainDecision(MainOutcome.AlreadyMain, location.LocationCode, location.DisplayName);

        var other = mains.FirstOrDefault();
        if (other != null)
        {
            // The agent's home centre stays whatever it is; a roster run is not a
            // transfer. Resolve the display name for the warning, falling back to
            // the raw code when the location row is gone or inactive.
            var otherLoc = await _db.WicLocations.FirstOrDefaultAsync(l =>
                l.LocationCode == other.LocationCode || l.LocationCodeLegacy == other.LocationCode);
            return new MainDecision(MainOutcome.ExistingOtherMain,
                other.LocationCode, otherLoc?.DisplayName ?? other.LocationCode);
        }

        return new MainDecision(
            confirmCreateMain ? MainOutcome.Created : MainOutcome.NotCreated, null, null);
    }

    /// <summary>
    /// The API-level (English) warning text for a MAIN decision, or null when the
    /// call is silent about MAIN. Exact wording is part of the contract with the
    /// UI and with scripts: "MAIN_UNCHANGED: agent already has main centre X".
    /// </summary>
    private static string? MainWarningText(MainDecision d, WicLocation? location) => d.Outcome switch
    {
        MainOutcome.ExistingOtherMain =>
            $"MAIN_UNCHANGED: agent already has main centre {d.ExistingMainName}",
        MainOutcome.NotCreated =>
            $"MAIN_NOT_CREATED: agent has no main centre; confirmCreateMain was not set, " +
            $"so {location?.DisplayName} was NOT set as the permanent main WIC",
        _ => null,
    };

    /// <summary>Machine-readable outcome for the UI (localised there), null for a plain roster.</summary>
    private static string? MainActionCode(MainOutcome o) => o switch
    {
        MainOutcome.AlreadyMain       => "already_main",
        MainOutcome.ExistingOtherMain => "unchanged",
        MainOutcome.Created           => "created",
        MainOutcome.NotCreated        => "not_created",
        _                             => null,
    };

    /// <summary>The centre a MAIN outcome refers to — created/not-created: the generated one,
    /// already-main: the same centre, unchanged: the agent's existing home centre.</summary>
    private static string? MainCentreName(MainDecision d, WicLocation? location) => d.Outcome switch
    {
        MainOutcome.ExistingOtherMain => d.ExistingMainName,
        MainOutcome.AlreadyMain       => d.ExistingMainName ?? location?.DisplayName,
        MainOutcome.Created or MainOutcome.NotCreated => location?.DisplayName,
        _                             => null,
    };

    // -- planning ------------------------------------------------------------

    private async Task<Plan> BuildPlanAsync(PlanInput input, bool overwrite)
    {
        // Public holidays exactly the way PublicHolidayService resolves them for
        // an agent: national holidays plus the employee's own Bundesland.
        // Filtered in-memory via PublicHolidayResolver.Applies (Mojibake-
        // resistant) rather than a SQL-translated `==` — see Mojibake.cs.
        var holidaysInRange = await _db.PublicHolidays
            .Where(h => h.HolidayDate >= input.From && h.HolidayDate <= input.To)
            .ToListAsync();
        var holidays = holidaysInRange
            .Where(h => PublicHolidayResolver.Applies(h, input.Emp.Bundesland))
            .GroupBy(h => h.HolidayDate)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var existingDates = (await _db.ShiftEntries
                .Where(x => x.EmployeeId == input.Emp.EmployeeId
                         && x.ShiftDate >= input.From && x.ShiftDate <= input.To)
                .Select(x => new { x.Id, x.ShiftDate, x.ShiftType, x.SourceModule, x.AgentTask, x.ManualChangeBy })
                .ToListAsync())
            .GroupBy(x => x.ShiftDate)
            .ToDictionary(g => g.Key, g => g.First());

        var toCreate   = new List<PlanRow>();
        var toReplace  = new List<ReplacePlan>();
        var skippedHol = new List<RosterSkippedHoliday>();
        var collisions = new List<RosterCollision>();
        var skippedWeekends = 0;

        // WIC mode: opening hours for the chosen location, resolved per date the
        // same way the rest of the system does it (latest EffectiveFrom <= date,
        // legacy location code included). Read from the system, never typed in.
        List<WicOpeningHour>? wicHours = null;
        if (input.Location != null)
            wicHours = await _db.WicOpeningHours.ToListAsync();

        for (var d = input.From; d <= input.To; d = d.AddDays(1))
        {
            // Weekends are always skipped — the same rule ShiftSyncService uses.
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                skippedWeekends++;
                continue;
            }
            // In WIC mode every weekday is a working day; the location's opening
            // days decide WIC vs BO, not the WorkingDays checkboxes.
            if (input.Location == null && !input.WorkingDays.Contains((int)d.DayOfWeek))
                continue;
            if (holidays.TryGetValue(d, out var holidayName))
            {
                skippedHol.Add(new RosterSkippedHoliday(d.ToString("yyyy-MM-dd"), holidayName));
                continue;
            }
            // Decide what the generated row for this date would be.
            PlanRow generated;
            if (input.Location == null)
            {
                generated = new PlanRow(d, "WORKING", input.Start ?? "08:00", input.End ?? "17:00");
            }
            else
            {
                var hours = WicHoursResolver.Resolve(wicHours!,
                    input.Location.LocationCode, input.Location.LocationCodeLegacy,
                    (int)d.DayOfWeek, d);
                // Two-interval centres: duty spans the break (WicHoursResolver.DutyWindow).
                // Taking hours.CloseTime alone used to end duty at the first close, leaving
                // the afternoon block of Augsburg/Essenbach/Hannover/München/Quickborn
                // looking unstaffed on the minute-based card metric.
                var (dutyStart, dutyEnd) = WicHoursResolver.DutyWindow(hours);
                generated = hours is { IsClosed: false }
                    // WIC day: the centre's OWN opening hours for this date win —
                    // an explicit request override is only a last-resort fallback,
                    // same as WicShiftService.CreateAssignmentAsync's time-source
                    // priority. A single fixed input.Start smeared across every
                    // day would be wrong for a location with different hours on
                    // different weekdays.
                    ? new PlanRow(d, "WIC",
                        dutyStart ?? input.Start ?? "08:00", dutyEnd ?? input.End ?? "17:00")
                    : new PlanRow(d, "BO", input.Start ?? "08:00", input.End ?? "17:00");
            }

            if (existingDates.TryGetValue(d, out var existing))
            {
                // Default: never touch an existing row. Overwrite mode replaces
                // everything EXCEPT protected rows (IsProtectedFromOverwrite —
                // absences, training/VWIC, WIC duty elsewhere) — those stay
                // skipped and are reported as collisions either way.
                var isProtected = IsProtectedFromOverwrite(existing.ShiftType, existing.AgentTask, input.Location,
                        isManualChange: !string.IsNullOrWhiteSpace(existing.ManualChangeBy));
                if (!overwrite || isProtected)
                {
                    collisions.Add(new RosterCollision(
                        d.ToString("yyyy-MM-dd"), existing.ShiftType, existing.SourceModule, isProtected));
                    continue;
                }
                toReplace.Add(new ReplacePlan(generated, existing.Id,
                    existing.ShiftType, existing.SourceModule));
                continue;
            }

            toCreate.Add(generated);
        }

        return new Plan(toCreate, toReplace, skippedWeekends, skippedHol, collisions);
    }

    private static RosterPreviewResult ToPreview(PlanInput input, Plan plan, MainDecision main)
    {
        // Created and replaced rows together are "what the agent's calendar will
        // show" — first/last date and the WIC/BO split count both.
        var allRows = plan.ToCreate.Select(r => r.Date)
            .Concat(plan.ToReplace.Select(r => r.Row.Date))
            .OrderBy(d => d).ToList();
        var allKinds = plan.ToCreate.Select(r => r.Kind)
            .Concat(plan.ToReplace.Select(r => r.Row.Kind)).ToList();
        return new(input.Emp.EmployeeId, input.Emp.FullName,
            plan.ToCreate.Count,
            allRows.Count > 0 ? allRows.First().ToString("yyyy-MM-dd") : null,
            allRows.Count > 0 ? allRows.Last().ToString("yyyy-MM-dd")  : null,
            plan.SkippedWeekends, plan.Holidays, plan.Collisions,
            plan.ToCreate.Select(r => new RosterPlanRow(
                r.Date.ToString("yyyy-MM-dd"), r.Start, r.End, r.Kind)).ToList(),
            allKinds.Count(k => k == "WIC"),
            allKinds.Count(k => k == "BO"),
            input.Location?.LocationCode, input.Location?.DisplayName,
            plan.ToReplace.Count,
            plan.ToReplace.Select(r => new RosterReplacement(
                r.Row.Date.ToString("yyyy-MM-dd"), r.ExistingType, r.ExistingSourceModule,
                r.Row.Kind, r.Row.Start, r.Row.End)).ToList(),
            main.ExistingMainCode, main.ExistingMainName,
            main.WouldCreateMain, MainWarningText(main, input.Location));
    }

    // -- public operations ---------------------------------------------------

    public async Task<RosterPreviewResult> PreviewAsync(RosterRequest req)
    {
        var input = await ValidateAsync(req);
        var plan  = await BuildPlanAsync(input, req.Overwrite);
        var main  = await ResolveMainAsync(input.Location, input.Emp.FullName, req.ConfirmCreateMain);
        return ToPreview(input, plan, main);
    }

    public async Task<RosterGenerateResult> GenerateAsync(RosterRequest req,
        string? createdByKid, string? createdByName)
    {
        var input = await ValidateAsync(req);

        // The plan is rebuilt inside the transaction so a row created between the
        // caller's preview and this write is still handled by the same rules
        // (skipped — or replaced, when Overwrite is on).
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var plan = await BuildPlanAsync(input, req.Overwrite);

            var batch = new RosterBatch
            {
                EmployeeId      = input.Emp.EmployeeId,
                FullName        = input.Emp.FullName,
                DateFrom        = input.From,
                DateTo          = input.To,
                // Informational only (RosterBatch.ShiftStart/End are non-nullable) —
                // WIC-mode rows themselves carry each day's own centre hours.
                ShiftStart      = input.Start ?? "08:00",
                ShiftEnd        = input.End ?? "17:00",
                WorkingDays     = string.Join(",", input.WorkingDays.OrderBy(d => d)),
                AgentTask       = input.AgentTask,
                LocationCode    = input.Location?.LocationCode,
                RowCount        = plan.ToCreate.Count,
                SkippedExisting = plan.Collisions.Count,
                SkippedHolidays = plan.Holidays.Count,
                ReplacedExisting = plan.ToReplace.Count,
                CreatedByKid    = createdByKid,
                CreatedByName   = createdByName,
                CreatedAt       = DateTime.UtcNow,
            };
            _db.RosterBatches.Add(batch);
            await _db.SaveChangesAsync(); // batch.Id needed as SourceId below

            // AgentTask on WIC_DUTY rows is the location DisplayName truncated to the
            // 20-char column — same convention WicShiftService.CreateAssignmentAsync uses.
            var wicAgentTask = input.Location == null ? null
                : (input.Location.DisplayName.Length > 20
                    ? input.Location.DisplayName[..20]
                    : input.Location.DisplayName);

            // Shape a ShiftEntry into exactly what this batch generates for the
            // given plan row. Used for new rows and for rows replaced in
            // Overwrite mode, so both paths can never drift apart.
            //   WIC:     ShiftType=WIC_DUTY, IsWicDuty, AgentTask=location name.
            //   WORKING: RawValue="HH:mm - HH:mm", AgentTask from the request.
            //   BO:      ShiftType=WORKING, RawValue="BO" (how back-office days
            //            look in ShiftEntries today).
            void ApplyGenerated(ShiftEntry e, PlanRow row)
            {
                e.RawValue         = row.Kind switch
                {
                    "WIC" => null,
                    "BO"  => "BO",
                    _     => $"{row.Start} - {row.End}",
                };
                e.ShiftType        = row.Kind == "WIC" ? ShiftTypes.WicDuty : ShiftTypes.Working;
                e.ShiftStart       = row.Start;
                e.ShiftEnd         = row.End;
                e.IsWicDuty        = row.Kind == "WIC";
                e.SourceSheet      = SourceSheetName;
                e.AgentTask        = row.Kind == "WIC" ? wicAgentTask
                                   : row.Kind == "BO"  ? null : input.AgentTask;
                e.LocationId       = null;
                e.AssignmentStatus = null;
                e.PreviousStatus   = null;
                e.AutoGenerated    = true;
                e.SourceModule     = SourceModuleName;
                e.SourceId         = batch.Id;
            }

            // WicShiftEntry upsert — unique on (EmployeeId, ShiftDate, SupportLocation).
            async Task UpsertWicEntryAsync(PlanRow row)
            {
                var wic = await _db.WicShiftEntries.FirstOrDefaultAsync(w =>
                    w.EmployeeId == input.Emp.EmployeeId &&
                    w.ShiftDate == row.Date &&
                    w.SupportLocation == input.Location!.DisplayName);
                if (wic != null)
                {
                    wic.WorkingShift = $"{row.Start}-{row.End}";
                    wic.LocationCode = input.Location!.LocationCode;
                    wic.IsOnSite     = true;
                    wic.IsGSDDay     = false;
                    wic.Task         = "WIC";
                }
                else
                {
                    _db.WicShiftEntries.Add(new WicShiftEntry
                    {
                        EmployeeId      = input.Emp.EmployeeId,
                        ShiftDate       = row.Date,
                        DayOfWeek       = row.Date.DayOfWeek.ToString(),
                        SupportLocation = input.Location!.DisplayName,
                        LocationCode    = input.Location!.LocationCode,
                        IsOnSite        = true,
                        IsGSDDay        = false,
                        IsOffDay        = false,
                        WorkingShift    = $"{row.Start}-{row.End}",
                        Task            = "WIC",
                    });
                }
            }

            foreach (var row in plan.ToCreate)
            {
                var entry = new ShiftEntry
                {
                    EmployeeId = input.Emp.EmployeeId,
                    ShiftDate  = row.Date,
                };
                ApplyGenerated(entry, row);
                _db.ShiftEntries.Add(entry);
                if (row.Kind == "WIC")
                    await UpsertWicEntryAsync(row);
            }

            // Overwrite mode: replace the planned existing rows. Every replaced
            // row is snapshotted first (with its WicShiftEntries rows when it was
            // a WIC_DUTY) so deleting the batch can restore the pre-batch state.
            var replaced = 0;
            if (plan.ToReplace.Count > 0)
            {
                var snapshots    = new List<ShiftEntrySnapshot>();
                var replaceIds   = plan.ToReplace.Select(r => r.ExistingId).ToList();
                var existingRows = await _db.ShiftEntries
                    .Where(x => replaceIds.Contains(x.Id))
                    .ToListAsync();
                var byId = existingRows.ToDictionary(x => x.Id);

                foreach (var rep in plan.ToReplace)
                {
                    // Vanished between plan and write (same tx, so practically
                    // never): fall back to inserting a fresh row.
                    if (!byId.TryGetValue(rep.ExistingId, out var row))
                    {
                        var fresh = new ShiftEntry
                        {
                            EmployeeId = input.Emp.EmployeeId,
                            ShiftDate  = rep.Row.Date,
                        };
                        ApplyGenerated(fresh, rep.Row);
                        _db.ShiftEntries.Add(fresh);
                        if (rep.Row.Kind == "WIC")
                            await UpsertWicEntryAsync(rep.Row);
                        continue;
                    }

                    var wicSnaps = new List<WicEntrySnapshot>();
                    if (_db.PreserveManualShift(row, "Roster overwrite")) continue;
                    if (row.ShiftType == ShiftTypes.WicDuty)
                    {
                        // The replaced row was WIC duty: its WicShiftEntries rows
                        // become stale (different location, or the day is now
                        // BO/WORKING). Snapshot them for restore, then remove —
                        // a WIC generated row re-creates the right one via the
                        // upsert below.
                        var wicRows = await _db.WicShiftEntries
                            .Where(w => w.EmployeeId == row.EmployeeId &&
                                        w.ShiftDate == row.ShiftDate)
                            .ToListAsync();
                        wicSnaps.AddRange(wicRows.Select(w => new WicEntrySnapshot(
                            w.DayOfWeek, w.SupportLocation, w.WicOpeningHours,
                            w.WorkingShift, w.IsOnSite, w.IsGSDDay, w.IsOffDay,
                            w.Task, w.LocationCode)));
                        _db.WicShiftEntries.RemoveRange(wicRows);
                    }

                    snapshots.Add(new ShiftEntrySnapshot(
                        row.EmployeeId, row.ShiftDate.ToString("yyyy-MM-dd"),
                        row.RawValue, row.ShiftType, row.ShiftStart, row.ShiftEnd,
                        row.IsWicDuty, row.SourceSheet, row.AgentTask, row.LocationId,
                        row.AssignmentStatus, row.AutoGenerated, row.SourceModule,
                        row.SourceId, row.PreviousStatus, wicSnaps));

                    ApplyGenerated(row, rep.Row);
                    replaced++;
                    if (rep.Row.Kind == "WIC")
                        await UpsertWicEntryAsync(rep.Row);
                }

                batch.ReplacedRowsJson = snapshots.Count > 0
                    ? JsonSerializer.Serialize(snapshots)
                    : null;
            }
            // Permanent MAIN WIC assignment — NEVER created silently. This block
            // used to add a MAIN row whenever the agent had no active assignment
            // for the generated centre, so "Generate roster" quietly changed an
            // agent's permanent home centre (the coverage view only lists agents
            // with a WicAgentAssignments row, and no screen could create one).
            // Now:
            //   * an active MAIN at ANOTHER centre is never replaced, duplicated
            //     or deactivated — the shifts for the selected period are still
            //     generated and the caller gets MAIN_UNCHANGED (case 1);
            //   * an agent with NO MAIN gets one only with confirmCreateMain=true
            //     (the UI's confirmation dialog) (case 2).
            var mainDecision = await ResolveMainAsync(
                input.Location, input.Emp.FullName, req.ConfirmCreateMain);
            var assignmentCreated = false;
            if (mainDecision.Outcome == MainOutcome.Created)
            {
                _db.WicAgentAssignments.Add(new WicAgentAssignment
                {
                    LocationCode   = input.Location!.LocationCode,
                    EmployeeName   = input.Emp.FullName!,
                    AssignmentType = "MAIN",
                    IsActive       = true,
                });
                assignmentCreated = true;
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // First/last date and the WIC/BO split cover created AND replaced
            // rows — together they are what the agent's calendar will show.
            var allDates = plan.ToCreate.Select(r => r.Date)
                .Concat(plan.ToReplace.Select(r => r.Row.Date))
                .OrderBy(d => d).ToList();
            var allKinds = plan.ToCreate.Select(r => r.Kind)
                .Concat(plan.ToReplace.Select(r => r.Row.Kind)).ToList();
            return new RosterGenerateResult(
                batch.Id, input.Emp.EmployeeId, plan.ToCreate.Count, plan.Collisions.Count,
                allDates.Count > 0 ? allDates.First().ToString("yyyy-MM-dd") : null,
                allDates.Count > 0 ? allDates.Last().ToString("yyyy-MM-dd")  : null,
                allKinds.Count(k => k == "WIC"),
                allKinds.Count(k => k == "BO"),
                assignmentCreated, replaced,
                MainActionCode(mainDecision.Outcome),
                MainWarningText(mainDecision, input.Location),
                MainCentreName(mainDecision, input.Location));
        });
    }

    public record LocationCoverageAgentResult(
        string EmployeeId, string EmployeeName, string AssignmentType, int DaysAssigned, int BatchId);

    public record LocationCoverageResult(
        string LocationCode, string LocationName, int DaysConsidered, int DaysAlreadyCovered,
        List<LocationCoverageAgentResult> Assignments, int DaysStillGap,
        bool CountMismatch = false);

    private static (TimeOnly s, TimeOnly e)? ParseWorkingShift(string? ws)
    {
        if (string.IsNullOrWhiteSpace(ws)) return null;
        var parts = ws.Split('-');
        if (parts.Length < 2) return null;
        return TimeOnly.TryParse(parts[0].Trim(), out var s) && TimeOnly.TryParse(parts[1].Trim(), out var e) ? (s, e) : null;
    }

    private static List<(TimeOnly s, TimeOnly e)> HoursIntervals(WicOpeningHour? h)
    {
        var list = new List<(TimeOnly, TimeOnly)>();
        if (h == null || h.IsClosed) return list;
        if (TimeOnly.TryParse(h.OpenTime, out var s1) && TimeOnly.TryParse(h.CloseTime, out var e1)) list.Add((s1, e1));
        if (TimeOnly.TryParse(h.OpenTime2, out var s2) && TimeOnly.TryParse(h.CloseTime2, out var e2)) list.Add((s2, e2));
        return list;
    }

    /// <summary>
    /// Permanent WIC-priority rule, extended to the whole assignment list: for
    /// every open day at this location that isn't already covered, walk the
    /// assigned agents in order — MAIN first, then BACKUP in assignment order
    /// — and roster the first one who is actually available. A generic GSD/BO/
    /// no-task WORKING shift counts as available and gets moved to WIC duty,
    /// same as the single-MAIN priority rule in BuildPlanAsync. Absence,
    /// TRAINING, a VWIC-tasked WORKING shift, and WIC duty elsewhere at
    /// overlapping hours count as unavailable — WIC duty elsewhere at
    /// NON-overlapping hours does not (the Demmin dual-centre case), so an
    /// agent can end up holding two WIC commitments the same day.
    ///
    /// Reuses WicShiftService.CreateAssignmentAsync per (agent, day) for the
    /// actual write — it already resolves the day's opening hours and checks
    /// overlap against the agent's other on-site WIC commitments that day, so
    /// the dual-centre case falls out of the existing, already-tested logic
    /// rather than a second copy of it here. One RosterBatch is created per
    /// agent who actually receives a row (never one for an agent who gets
    /// nothing), tagged SourceModule="Roster" so DeleteBatchAsync can undo it
    /// exactly like any other generated batch.
    /// </summary>
    public async Task<LocationCoverageResult> GenerateLocationCoverageAsync(
        string locationCode, DateOnly from, DateOnly to, string? createdByKid, string? createdByName)
    {
        var location = await _db.WicLocations.FirstOrDefaultAsync(l => l.LocationCode == locationCode && l.IsActive);
        if (location == null)
            throw new RosterValidationException("Unknown or inactive WIC location: " + locationCode);

        var assignments = await _db.WicAgentAssignments
            .Where(a => a.IsActive && (a.AssignmentType == "MAIN" || a.AssignmentType == "BACKUP") &&
                        (a.LocationCode == location.LocationCode ||
                         (location.LocationCodeLegacy != null && a.LocationCode == location.LocationCodeLegacy)))
            .OrderBy(a => a.AssignmentType == "MAIN" ? 0 : 1)
            .ThenBy(a => a.Id) // assignment order among BACKUPs
            .ToListAsync();

        var candidates = new List<(WicAgentAssignment Assignment, Employee Emp)>();
        foreach (var a in assignments)
        {
            var emp = await _db.Employees.FirstOrDefaultAsync(e => e.FullName == a.EmployeeName && e.IsActive);
            if (emp != null) candidates.Add((a, emp));
        }
        if (candidates.Count == 0)
            return new LocationCoverageResult(location.LocationCode, location.DisplayName, 0, 0, new(), 0);

        var allHours = await _db.WicOpeningHours.ToListAsync();
        var nationalHolidays = new HashSet<DateOnly>(
            await _db.PublicHolidays.Where(h => h.IsNational && h.HolidayDate >= from && h.HolidayDate <= to)
                .Select(h => h.HolidayDate).ToListAsync());

        // Cover signal — the exact WicCoverageGapService.ComputeCoveredDates helper,
        // not a second copy of the definition (Part 2: never let the generator's
        // idea of "covered" drift from the coverage-gaps endpoint's).
        var onSiteEntries = await _db.WicShiftEntries.Where(w => w.ShiftDate >= from && w.ShiftDate <= to && w.IsOnSite).ToListAsync();
        var wicDutyElsewhere = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to && s.ShiftType == ShiftTypes.WicDuty && s.AgentTask != null && s.AgentTask != "")
            .ToListAsync();
        var onSiteEmployeeShiftEntries = await WicCoverageGapService.GetShiftEntriesForOnSiteEmployeesAsync(_db, onSiteEntries, from, to);
        var coveredDates = WicCoverageGapService.ComputeCoveredDates(onSiteEntries, wicDutyElsewhere, location, onSiteEmployeeShiftEntries);

        var empIds = candidates.Select(c => c.Emp.EmployeeId).ToList();
        var shiftByEmpDate = (await _db.ShiftEntries
                .Where(s => empIds.Contains(s.EmployeeId) && s.ShiftDate >= from && s.ShiftDate <= to).ToListAsync())
            .GroupBy(s => (s.EmployeeId, s.ShiftDate))
            .ToDictionary(g => g.Key, g => ShiftDuplicateResolver.BestShiftEntry(g));
        var wicByEmpDate = (await _db.WicShiftEntries
                .Where(w => empIds.Contains(w.EmployeeId) && w.ShiftDate >= from && w.ShiftDate <= to && w.IsOnSite).ToListAsync())
            .GroupBy(w => (w.EmployeeId, w.ShiftDate))
            .ToDictionary(g => g.Key, g => g.ToList());

        bool IsAvailable(string empId, DateOnly date, WicOpeningHour? targetHours)
        {
            shiftByEmpDate.TryGetValue((empId, date), out var shift);
            // No ShiftEntries row at all is UNKNOWN, not a verified "free" — there is no
            // absence, no working shift, nothing recorded either way. Unknown is not the
            // same as free: without a row we cannot rule out an absence that was never
            // imported, so the walk must not roster someone here on a guess.
            if (shift == null) return false; // unknown (no record) -> NOT available
            if (_db.PreserveManualShift(shift, "Location coverage generation")) return false;
            if (ProtectedAbsenceTypes.Contains(shift.ShiftType)) return false;
            if (string.Equals(shift.ShiftType, ShiftTypes.Training, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(shift.ShiftType, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(shift.AgentTask, "VWIC", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(shift.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase))
                return true; // WORKING (GSD/BO/no-task) -> available, gets moved to WIC duty

            // Already on WIC duty somewhere — available here too only if none of
            // their existing on-site blocks overlap this location's hours today.
            if (targetHours == null) return false;
            var targetIntervals = HoursIntervals(targetHours);
            if (!wicByEmpDate.TryGetValue((empId, date), out var existingRows)) return true;
            foreach (var w in existingRows)
            {
                var iv = ParseWorkingShift(w.WorkingShift);
                if (iv == null) continue;
                foreach (var (ts, te) in targetIntervals)
                    if (iv.Value.s < te && ts < iv.Value.e) return false; // overlap
            }
            return true;
        }

        var assignedDatesByEmp = new Dictionary<string, List<DateOnly>>();
        int daysConsidered = 0, daysAlreadyCovered = 0, daysStillGap = 0;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (nationalHolidays.Contains(d)) continue;
            var hours = WicHoursResolver.Resolve(allHours, location.LocationCode, location.LocationCodeLegacy, (int)d.DayOfWeek, d);
            bool isOpen = hours != null && !hours.IsClosed && !(string.IsNullOrEmpty(hours.OpenTime) && string.IsNullOrEmpty(hours.CloseTime));
            if (!isOpen) continue;
            daysConsidered++;
            if (coveredDates.Contains(d)) { daysAlreadyCovered++; continue; }

            bool assigned = false;
            foreach (var (_, emp) in candidates)
            {
                if (!IsAvailable(emp.EmployeeId, d, hours)) continue;
                if (!assignedDatesByEmp.TryGetValue(emp.EmployeeId, out var list))
                    assignedDatesByEmp[emp.EmployeeId] = list = new();
                list.Add(d);
                assigned = true;
                break;
            }
            if (!assigned) daysStillGap++;
        }

        var results = new List<LocationCoverageAgentResult>();
        foreach (var (empId, dates) in assignedDatesByEmp)
        {
            var (assignment, emp) = candidates.First(c => c.Emp.EmployeeId == empId);
            var batch = new RosterBatch
            {
                EmployeeId   = emp.EmployeeId,
                FullName     = emp.FullName,
                DateFrom     = dates.Min(),
                DateTo       = dates.Max(),
                ShiftStart   = "08:00", // informational — actual rows carry each day's own centre hours
                ShiftEnd     = "17:00",
                WorkingDays  = "1,2,3,4,5",
                LocationCode = location.LocationCode,
                RowCount     = dates.Count,
                CreatedByKid  = createdByKid,
                CreatedByName = createdByName,
                CreatedAt     = DateTime.UtcNow,
            };
            _db.RosterBatches.Add(batch);
            await _db.SaveChangesAsync(); // batch.Id needed as SourceId below

            var written = 0;
            foreach (var d in dates.OrderBy(x => x))
            {
                var req = new CreateAssignmentRequest(emp.EmployeeId, location.LocationCode, d.ToString("yyyy-MM-dd"), null, null);
                var result = await _wicShiftService.CreateAssignmentAsync(req, _db, SourceModuleName, batch.Id);
                var value = (result as IValueHttpResult)?.Value;
                if (value?.GetType().GetProperty("success")?.GetValue(value) is true &&
                    value.GetType().GetProperty("skipped")?.GetValue(value) is not true)
                    written++;
                else
                    Console.Error.WriteLine($"[RosterService] Assignment not written: {emp.EmployeeId} / {d:yyyy-MM-dd}");
            }
            batch.RowCount = written;
            await _db.SaveChangesAsync();

            results.Add(new LocationCoverageAgentResult(
                emp.EmployeeId, emp.FullName ?? emp.EmployeeId, assignment.AssignmentType, written, batch.Id));
        }

        // Verify the tally just computed against WicCoverageGapService's own
        // ground truth for this location+range, now that the writes above are
        // committed — the exact check Part 2 asked for. Both sides share
        // ComputeCoveredDates/GetOpenDates so they cannot drift on their own,
        // but if daysStillGap and the endpoint's count ever disagree (e.g. a
        // future edit to one side and not the other), the endpoint's number
        // wins and the mismatch is logged loudly rather than silently reported.
        var verifiedStillGap = await _gapService.GetStillGapCountAsync(location.LocationCode, from, to);
        bool countMismatch = verifiedStillGap != daysStillGap;
        if (countMismatch)
        {
            Console.Error.WriteLine(
                $"[RosterService.GenerateLocationCoverageAsync] Coverage count MISMATCH for " +
                $"{location.DisplayName} ({from:yyyy-MM-dd}..{to:yyyy-MM-dd}): generator counted " +
                $"{daysStillGap} day(s) still gapped, WicCoverageGapService counts {verifiedStillGap}. " +
                "Reporting the endpoint's number as the true count.");
        }

        return new LocationCoverageResult(
            location.LocationCode, location.DisplayName, daysConsidered, daysAlreadyCovered, results,
            verifiedStillGap, countMismatch);
    }

    /// <summary>
    /// Active WIC locations for the roster location dropdown. OpeningDays are the
    /// weekdays the location is open per WicOpeningHours (EffectiveFrom- and
    /// legacy-code aware, resolved as of today).
    /// </summary>
    public async Task<List<RosterLocationDto>> GetLocationsAsync()
    {
        var locations = await _db.WicLocations
            .Where(l => l.IsActive)
            .OrderBy(l => l.Country).ThenBy(l => l.City).ThenBy(l => l.DisplayName)
            .ToListAsync();
        var allHours = await _db.WicOpeningHours.ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        string[] labels = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

        return locations.Select(l =>
        {
            var openDays = Enumerable.Range(0, 7)
                .Where(dow =>
                {
                    var h = WicHoursResolver.Resolve(allHours, l.LocationCode, l.LocationCodeLegacy, dow, today);
                    return h != null && !h.IsClosed;
                })
                .ToArray();
            var label = openDays.Length == 0
                ? "—"
                : string.Join(' ', openDays.OrderBy(d => d == 0 ? 7 : d).Select(d => labels[d]));
            return new RosterLocationDto(
                l.LocationCode, l.DisplayName, l.City, l.Country, openDays, label);
        }).ToList();
    }

    public async Task<List<RosterBatchDto>> GetBatchesAsync()
    {
        var rows = await _db.RosterBatches
            .Where(b => b.DeletedAt == null)
            .OrderByDescending(b => b.Id)
            .ToListAsync();

        var codes = rows.Where(b => b.LocationCode != null)
            .Select(b => b.LocationCode!).Distinct().ToList();
        var locNames = await _db.WicLocations
            .Where(l => codes.Contains(l.LocationCode))
            .ToDictionaryAsync(l => l.LocationCode, l => l.DisplayName);

        return rows.Select(b => new RosterBatchDto(
            b.Id, b.EmployeeId, b.FullName,
            b.DateFrom.ToString("yyyy-MM-dd"), b.DateTo.ToString("yyyy-MM-dd"),
            b.ShiftStart, b.ShiftEnd, b.WorkingDays, b.AgentTask,
            b.LocationCode,
            b.LocationCode != null && locNames.TryGetValue(b.LocationCode, out var n) ? n : null,
            b.RowCount, b.SkippedExisting, b.SkippedHolidays,
            b.ReplacedExisting, b.ReplacedRowsJson != null,
            b.CreatedByKid, b.CreatedByName,
            BerlinTime.FromUtc(b.CreatedAt).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).ToList();
    }

    /// <summary>
    /// Missing-roster check over [today, today + days - 1] (days clamped to 90).
    /// Three directions:
    ///   1. active employees with zero ShiftEntries in the window (no roster);
    ///   2. ShiftEntries in the window whose EmployeeId has no ACTIVE Employees row;
    ///   3. per active employee, the individual dates in the window with no row at
    ///      all — catches sparse/partial coverage (e.g. the Erdal Coskun case: some
    ///      rows exist, but specific weekdays are missing) that direction 1's
    ///      all-or-nothing check cannot see.
    /// Read-only.
    /// </summary>
    public async Task<MissingRosterResult> GetMissingRosterAsync(int days)
    {
        days = Math.Clamp(days, 1, 90);
        var from = DateOnly.FromDateTime(DateTime.Today);
        var to   = from.AddDays(days - 1);

        var activeEmployees = await _db.Employees
            .Where(e => e.IsActive)
            .Select(e => new { e.EmployeeId, e.FullName, e.TeamLeadName, e.PrimaryRole })
            .ToListAsync();

        var windowGroups = await _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to)
            .GroupBy(s => s.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                RowCount   = g.Count(),
                FirstDate  = g.Min(x => x.ShiftDate),
                LastDate   = g.Max(x => x.ShiftDate),
            })
            .ToListAsync();

        var withRoster = new HashSet<string>(
            windowGroups.Select(w => w.EmployeeId), StringComparer.OrdinalIgnoreCase);
        var activeIds = new HashSet<string>(
            activeEmployees.Select(e => e.EmployeeId), StringComparer.OrdinalIgnoreCase);

        // Last rostered date ever, for context on the missing list (one extra query).
        var missingIds = activeEmployees
            .Where(e => !withRoster.Contains(e.EmployeeId))
            .Select(e => e.EmployeeId).ToList();
        var lastDates = await _db.ShiftEntries
            .Where(s => missingIds.Contains(s.EmployeeId))
            .GroupBy(s => s.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, LastDate = g.Max(x => x.ShiftDate) })
            .ToListAsync();
        var lastDateByEmp = lastDates.ToDictionary(x => x.EmployeeId, x => x.LastDate,
            StringComparer.OrdinalIgnoreCase);

        var missing = activeEmployees
            .Where(e => !withRoster.Contains(e.EmployeeId))
            .OrderBy(e => e.TeamLeadName).ThenBy(e => e.FullName ?? e.EmployeeId)
            .Select(e => new MissingRosterAgent(
                e.EmployeeId, e.FullName, e.TeamLeadName, e.PrimaryRole,
                lastDateByEmp.TryGetValue(e.EmployeeId, out var ld)
                    ? ld.ToString("yyyy-MM-dd") : null))
            .ToList();

        // Orphans: window entries with no ACTIVE Employees row. Distinguish
        // "inactive row exists" from "no row at all" — the fix differs.
        var orphanIds = windowGroups
            .Where(w => !activeIds.Contains(w.EmployeeId))
            .Select(w => w.EmployeeId).ToList();
        var knownInactive = orphanIds.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                await _db.Employees
                    .Where(e => orphanIds.Contains(e.EmployeeId))
                    .Select(e => e.EmployeeId)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

        var orphans = windowGroups
            .Where(w => !activeIds.Contains(w.EmployeeId))
            .OrderByDescending(w => w.RowCount)
            .Select(w => new OrphanShiftEntryGroup(
                w.EmployeeId, w.RowCount,
                w.FirstDate.ToString("yyyy-MM-dd"), w.LastDate.ToString("yyyy-MM-dd"),
                knownInactive.Contains(w.EmployeeId)))
            .ToList();

        // Per-employee date gaps: catches partial/sparse coverage (e.g. weekday
        // rows that stop mid-window) that the all-or-nothing MissingRoster check
        // above cannot see. Bounded to active employees to keep this bounded at
        // (active employee count) x (window days).
        var activeIdList = activeEmployees.Select(e => e.EmployeeId).ToList();
        var existingDatesByEmp = (await _db.ShiftEntries
                .Where(s => activeIdList.Contains(s.EmployeeId) && s.ShiftDate >= from && s.ShiftDate <= to)
                .Select(s => new { s.EmployeeId, s.ShiftDate })
                .Distinct()
                .ToListAsync())
            .GroupBy(x => x.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ShiftDate).ToHashSet(),
                StringComparer.OrdinalIgnoreCase);

        var allDatesInWindow = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1)) allDatesInWindow.Add(d);

        var dateGaps = new List<EmployeeDateGaps>();
        foreach (var e in activeEmployees)
        {
            existingDatesByEmp.TryGetValue(e.EmployeeId, out var existing);
            existing ??= new HashSet<DateOnly>();
            var gapDates = allDatesInWindow
                .Where(d => !existing.Contains(d))
                .Select(d => d.ToString("yyyy-MM-dd"))
                .ToList();
            if (gapDates.Count > 0)
                dateGaps.Add(new EmployeeDateGaps(e.EmployeeId, e.FullName, gapDates));
        }
        dateGaps = dateGaps.OrderByDescending(g => g.MissingDates.Count)
            .ThenBy(g => g.FullName ?? g.EmployeeId).ToList();

        return new MissingRosterResult(
            days, from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), missing, orphans, dateGaps);
    }

    /// <summary>
    /// Deletes every ShiftEntry still owned by the batch (SourceModule="Roster",
    /// SourceId=batch id). Rows another module has since taken over (e.g.
    /// ShiftSyncService overwrote SourceModule with "SickLeave") are detached
    /// from the batch by design and are left untouched. The batch row itself is
    /// soft-deleted so the audit trail survives.
    /// Overwrite batches additionally restore the rows they replaced from the
    /// ReplacedRowsJson snapshot — unless something else has since claimed that
    /// employee+date, in which case the restore of that date is skipped (and
    /// reported as RestoreSkipped), never overwritten.
    /// </summary>
    public async Task<RosterDeleteResult?> DeleteBatchAsync(int batchId)
    {
        var batch = await _db.RosterBatches
            .FirstOrDefaultAsync(b => b.Id == batchId && b.DeletedAt == null);
        if (batch == null) return null;

        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var owned = await _db.ShiftEntries
                .Where(x => x.SourceId == batchId)
                .ToListAsync();

            var deletable = owned.Where(x => x.SourceModule == SourceModuleName &&
                !_db.PreserveManualShift(x, "Roster batch delete")).ToList();
            var detached  = owned.Count - deletable.Count;

            // WIC batches also wrote WicShiftEntries. They carry no SourceId, so match
            // them by employee + the deleted WIC_DUTY dates + the batch location.
            var wicRemoved = 0;
            if (batch.LocationCode != null)
            {
                var locName = await _db.WicLocations
                    .Where(l => l.LocationCode == batch.LocationCode)
                    .Select(l => l.DisplayName)
                    .FirstOrDefaultAsync();
                var wicDates = deletable
                    .Where(x => x.ShiftType == ShiftTypes.WicDuty)
                    .Select(x => x.ShiftDate).ToList();
                if (locName != null && wicDates.Count > 0)
                {
                    var wicRows = await _db.WicShiftEntries
                        .Where(w => w.EmployeeId == batch.EmployeeId &&
                                    w.SupportLocation == locName &&
                                    wicDates.Contains(w.ShiftDate))
                        .ToListAsync();
                    wicRemoved = wicRows.Count;
                    _db.WicShiftEntries.RemoveRange(wicRows);
                }
            }

            _db.ShiftEntries.RemoveRange(deletable);
            await _db.SaveChangesAsync(); // free the dates before the restore checks

            // Undo for overwrite batches: re-insert the snapshotted pre-batch
            // rows. A date another module has since claimed is skipped, never
            // overwritten — the UI reports that count so nobody expects a row
            // back that is not coming back.
            var restored = 0; var restoredWic = 0; var restoreSkipped = 0;
            if (!string.IsNullOrEmpty(batch.ReplacedRowsJson))
            {
                var snaps = JsonSerializer.Deserialize<List<ShiftEntrySnapshot>>(
                    batch.ReplacedRowsJson) ?? new List<ShiftEntrySnapshot>();
                foreach (var s in snaps)
                {
                    var date = DateOnly.Parse(s.ShiftDate, CultureInfo.InvariantCulture);
                    var occupied = await _db.ShiftEntries.AnyAsync(x =>
                        x.EmployeeId == s.EmployeeId && x.ShiftDate == date);
                    if (occupied) { restoreSkipped++; continue; }

                    _db.ShiftEntries.Add(new ShiftEntry
                    {
                        EmployeeId       = s.EmployeeId,
                        ShiftDate        = date,
                        RawValue         = s.RawValue,
                        ShiftType        = s.ShiftType,
                        ShiftStart       = s.ShiftStart,
                        ShiftEnd         = s.ShiftEnd,
                        IsWicDuty        = s.IsWicDuty,
                        SourceSheet      = s.SourceSheet,
                        AgentTask        = s.AgentTask,
                        LocationId       = s.LocationId,
                        AssignmentStatus = s.AssignmentStatus,
                        AutoGenerated    = s.AutoGenerated,
                        SourceModule     = s.SourceModule,
                        SourceId         = s.SourceId,
                        PreviousStatus   = s.PreviousStatus,
                    });
                    restored++;

                    foreach (var w in s.WicEntries)
                    {
                        var wicExists = await _db.WicShiftEntries.AnyAsync(x =>
                            x.EmployeeId == s.EmployeeId && x.ShiftDate == date &&
                            x.SupportLocation == w.SupportLocation);
                        if (wicExists) continue;
                        _db.WicShiftEntries.Add(new WicShiftEntry
                        {
                            EmployeeId      = s.EmployeeId,
                            ShiftDate       = date,
                            DayOfWeek       = w.DayOfWeek,
                            SupportLocation = w.SupportLocation,
                            WicOpeningHours = w.WicOpeningHours,
                            WorkingShift    = w.WorkingShift,
                            IsOnSite        = w.IsOnSite,
                            IsGSDDay        = w.IsGSDDay,
                            IsOffDay        = w.IsOffDay,
                            Task            = w.Task,
                            LocationCode    = w.LocationCode,
                        });
                        restoredWic++;
                    }
                }
            }

            batch.DeletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return new RosterDeleteResult(batchId, deletable.Count, detached, wicRemoved,
                restored, restoredWic, restoreSkipped);
        });
    }
}

public static class RosterEndpointMapper
{
    public static void MapRosterEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/roster").WithTags("Roster");

        grp.MapPost("/preview", async (RosterRequest req, HttpContext ctx, RosterService svc) =>
        {
            if (RosterAccess.DenyResult(ctx) is { } deny) return deny;
            try
            {
                return Results.Ok(await svc.PreviewAsync(req));
            }
            catch (RosterValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        grp.MapPost("/generate", async (RosterRequest req, HttpContext ctx, RosterService svc) =>
        {
            if (RosterAccess.DenyResult(ctx) is { } deny) return deny;
            try
            {
                return Results.Ok(await svc.GenerateAsync(req,
                    ctx.User.FindFirst(AuthClaims.Kid)?.Value,
                    ctx.User.FindFirst(AuthClaims.Name)?.Value));
            }
            catch (RosterValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        grp.MapGet("/locations", async (HttpContext ctx, RosterService svc) =>
        {
            if (RosterAccess.DenyResult(ctx) is { } deny) return deny;
            return Results.Ok(await svc.GetLocationsAsync());
        });

        grp.MapGet("/batches", async (HttpContext ctx, RosterService svc) =>
        {
            if (RosterAccess.DenyResult(ctx) is { } deny) return deny;
            return Results.Ok(await svc.GetBatchesAsync());
        });

        // Missing-roster warning: RTM / TEAM_LEAD / DEV only (RosterAccess gate —
        // an AGENT session gets 403 here even while Auth:EnforceAuthorization is off).
        grp.MapGet("/missing", async (int? days, HttpContext ctx, RosterService svc) =>
        {
            if (RosterAccess.DenyResult(ctx) is { } deny) return deny;
            return Results.Ok(await svc.GetMissingRosterAsync(days ?? 14));
        });

        grp.MapDelete("/batches/{id:int}", async (int id, HttpContext ctx, RosterService svc) =>
        {
            if (RosterAccess.DenyResult(ctx) is { } deny) return deny;
            var result = await svc.DeleteBatchAsync(id);
            return result == null
                ? Results.NotFound(new { error = "Batch not found or already deleted" })
                : Results.Ok(result);
        });

        // MAIN/BACKUP coverage walk for one WIC location — was previously only
        // reachable by directly calling RosterService.GenerateLocationCoverageAsync
        // (e.g. from a one-off script), never from the running app. That gap was
        // the actual cause behind "the generator skipped the free backup": there
        // was no way to run this from the live API at all.
        grp.MapPost("/generate-location-coverage", async (LocationCoverageRequest req, HttpContext ctx, RosterService svc) =>
        {
            if (RosterAccess.DenyResult(ctx) is { } deny) return deny;
            if (string.IsNullOrWhiteSpace(req.LocationCode))
                return Results.BadRequest(new { error = "locationCode is required" });
            if (!DateOnly.TryParse(req.From, out var from) || !DateOnly.TryParse(req.To, out var to))
                return Results.BadRequest(new { error = "from and to must be valid dates (yyyy-MM-dd)" });
            if (to < from)
                return Results.BadRequest(new { error = "to must not be before from" });
            try
            {
                return Results.Ok(await svc.GenerateLocationCoverageAsync(req.LocationCode, from, to,
                    ctx.User.FindFirst(AuthClaims.Kid)?.Value,
                    ctx.User.FindFirst(AuthClaims.Name)?.Value));
            }
            catch (RosterValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

public record LocationCoverageRequest(string? LocationCode, string? From, string? To);
