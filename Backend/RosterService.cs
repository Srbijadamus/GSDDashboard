using System.Globalization;
using System.Text.Json;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
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
//   today (ShiftType=WORKING, RawValue="BO"). If the agent has no active
//   WicAgentAssignments row for the location, one is created (MAIN) so the
//   agent appears in the WIC coverage view — no screen can do that today.
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
    string? LocationCode = null, bool Overwrite = false);

public record RosterSkippedHoliday(string Date, string Name);
public record RosterCollision(string Date, string ShiftType, string? SourceModule);

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
    int ReplaceCount, List<RosterReplacement> Replacements);

public record RosterGenerateResult(
    int BatchId, string EmployeeId, int Created, int SkippedExisting,
    string? FirstDate, string? LastDate,
    int WicDays, int BoDays, bool AssignmentCreated, int Replaced);

public record RosterBatchDto(
    int Id, string EmployeeId, string? FullName,
    string DateFrom, string DateTo, string ShiftStart, string ShiftEnd,
    string WorkingDays, string? AgentTask,
    string? LocationCode, string? LocationName,
    int RowCount, int SkippedExisting, int SkippedHolidays,
    int ReplacedExisting, bool HasSnapshot,
    string? CreatedByKid, string? CreatedByName, DateTime CreatedAt);

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

public record MissingRosterResult(
    int Days, string From, string To,
    List<MissingRosterAgent> MissingRoster,
    List<OrphanShiftEntryGroup> OrphanEntries);

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
    public RosterService(GSDContext db) => _db = db;

    private sealed record PlanInput(Employee Emp, DateOnly From, DateOnly To,
        string Start, string End, HashSet<int> WorkingDays, string? AgentTask,
        WicLocation? Location);

    private sealed record PlanRow(DateOnly Date, string Kind, string Start, string End);

    // Overwrite mode: the generated row plus the existing ShiftEntry it replaces.
    private sealed record ReplacePlan(PlanRow Row, int ExistingId,
        string ExistingType, string? ExistingSourceModule);

    private sealed record Plan(List<PlanRow> ToCreate, List<ReplacePlan> ToReplace,
        int SkippedWeekends,
        List<RosterSkippedHoliday> Holidays, List<RosterCollision> Collisions);

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

        var start = NormalizeTime(req.ShiftStart, "shiftStart");
        var end   = NormalizeTime(req.ShiftEnd,   "shiftEnd");

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

        return new PlanInput(emp, from, to, start, end, days, task, location);
    }

    private static string NormalizeTime(string? v, string field)
    {
        if (string.IsNullOrWhiteSpace(v) ||
            !TimeOnly.TryParse(v.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            throw new RosterValidationException($"Invalid {field} (expected HH:mm)");
        return t.ToString("HH:mm");
    }

    // -- planning ------------------------------------------------------------

    private async Task<Plan> BuildPlanAsync(PlanInput input, bool overwrite)
    {
        // Public holidays exactly the way PublicHolidayService resolves them for
        // an agent: national holidays plus the employee's own Bundesland.
        var holidayQuery = _db.PublicHolidays
            .Where(h => h.HolidayDate >= input.From && h.HolidayDate <= input.To);
        holidayQuery = string.IsNullOrWhiteSpace(input.Emp.Bundesland)
            ? holidayQuery.Where(h => h.IsNational)
            : holidayQuery.Where(h => h.IsNational || h.Bundesland == input.Emp.Bundesland);
        var holidays = (await holidayQuery.ToListAsync())
            .GroupBy(h => h.HolidayDate)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var existingDates = (await _db.ShiftEntries
                .Where(x => x.EmployeeId == input.Emp.EmployeeId
                         && x.ShiftDate >= input.From && x.ShiftDate <= input.To)
                .Select(x => new { x.Id, x.ShiftDate, x.ShiftType, x.SourceModule })
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
                generated = new PlanRow(d, "WORKING", input.Start, input.End);
            }
            else
            {
                var hours = WicHoursResolver.Resolve(wicHours!,
                    input.Location.LocationCode, input.Location.LocationCodeLegacy,
                    (int)d.DayOfWeek, d);
                generated = hours is { IsClosed: false }
                    // WIC day: times come from the location's opening hours for this
                    // date, exactly like WicShiftService.CreateAssignmentAsync sets them.
                    ? new PlanRow(d, "WIC",
                        hours.OpenTime ?? input.Start, hours.CloseTime ?? input.End)
                    : new PlanRow(d, "BO", input.Start, input.End);
            }

            if (existingDates.TryGetValue(d, out var existing))
            {
                // Default: never touch an existing row. Overwrite mode replaces
                // everything EXCEPT protected absences (ProtectedAbsenceTypes) —
                // those stay skipped and are reported as collisions either way.
                if (!overwrite || ProtectedAbsenceTypes.Contains(existing.ShiftType))
                {
                    collisions.Add(new RosterCollision(
                        d.ToString("yyyy-MM-dd"), existing.ShiftType, existing.SourceModule));
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

    private static RosterPreviewResult ToPreview(PlanInput input, Plan plan)
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
                r.Row.Kind, r.Row.Start, r.Row.End)).ToList());
    }

    // -- public operations ---------------------------------------------------

    public async Task<RosterPreviewResult> PreviewAsync(RosterRequest req)
    {
        var input = await ValidateAsync(req);
        var plan  = await BuildPlanAsync(input, req.Overwrite);
        return ToPreview(input, plan);
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
                ShiftStart      = input.Start,
                ShiftEnd        = input.End,
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
            // The coverage view only lists agents with an active WicAgentAssignments
            // row and no screen can create one — add the MAIN row if it is missing.
            var assignmentCreated = false;
            if (input.Location != null && input.Emp.FullName != null)
            {
                var code   = input.Location.LocationCode;
                var legacy = input.Location.LocationCodeLegacy;
                var hasAssignment = await _db.WicAgentAssignments.AnyAsync(a =>
                    a.IsActive &&
                    a.EmployeeName == input.Emp.FullName &&
                    (a.LocationCode == code || (legacy != null && a.LocationCode == legacy)));
                if (!hasAssignment)
                {
                    _db.WicAgentAssignments.Add(new WicAgentAssignment
                    {
                        LocationCode   = code,
                        EmployeeName   = input.Emp.FullName,
                        AssignmentType = "MAIN",
                        IsActive       = true,
                    });
                    assignmentCreated = true;
                }
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
                assignmentCreated, replaced);
        });
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
            b.CreatedByKid, b.CreatedByName, b.CreatedAt)).ToList();
    }

    /// <summary>
    /// Missing-roster check over [today, today + days - 1].
    /// Two directions:
    ///   1. active employees with zero ShiftEntries in the window (no roster);
    ///   2. ShiftEntries in the window whose EmployeeId has no ACTIVE Employees row.
    /// Read-only; three queries total.
    /// </summary>
    public async Task<MissingRosterResult> GetMissingRosterAsync(int days)
    {
        days = Math.Clamp(days, 1, 60);
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

        return new MissingRosterResult(
            days, from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), missing, orphans);
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

            var deletable = owned.Where(x => x.SourceModule == SourceModuleName).ToList();
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
    }
}

