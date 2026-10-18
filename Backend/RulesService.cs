using System.Diagnostics;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SickLeaveModel = GSDDashboard.API.Data.Models.SickLeave;

namespace GSDDashboard.API.Modules.Rules;

/// <summary>
/// Rule-engine thresholds, read through IOptionsMonitor so a change in appsettings
/// ("Rules" section) takes effect WITHOUT a restart. Defaults mirror the values the
/// existing ShiftValidationService already assumes (11h rest, 6 consecutive days).
/// </summary>
public sealed class RulesOptions
{
    public int MinRestHours { get; set; } = 11;
    public int MaxConsecutiveDays { get; set; } = 6;
}

public static class RuleCodes
{
    public const string Overlap        = "R1";
    public const string Absence        = "R2";
    public const string OtherWic       = "R3";
    public const string Training       = "R4";
    public const string MinRest        = "R5";
    public const string Consecutive    = "R6";
    public const string DataInvalid    = "DATA_INVALID";
}

public static class RuleSeverity
{
    public const string Error   = "error";
    public const string Warning = "warning";
    public const string Info    = "info";
}

/// <summary>
/// One rule result. The backend always returns a code + machine-usable params + an
/// English fallback message; the frontend translates code+params into EN/DE. `ref`
/// is a non-personal pointer (ticket id or legal basis). Params NEVER contain a
/// name, KID or EmployeeId.
/// </summary>
public sealed record RuleWarning(
    string Code,
    string Severity,
    string Date,
    Dictionary<string, object?> Params,
    string Ref,
    string Message);

/// <summary>A proposed (not yet saved) shift to validate in context.</summary>
public sealed record ProposedShift(
    string? EmployeeId,
    string? Date,
    string? ShiftType,
    string? Start,
    string? End);

public sealed record RulesValidateRequest(
    string? From,
    string? To,
    string? Team,
    ProposedShift? Proposed);

public sealed record PersonWarnings(string EmployeeId, List<RuleWarning> Warnings);

public sealed record RulesSummary(int People, Dictionary<string, int> Warnings);

public sealed record RulesResponse(string From, string To, List<PersonWarnings> People, RulesSummary Summary);

/// <summary>
/// Pure rule evaluation for ONE person. Kept static and DB-free so every rule and its
/// boundaries can be unit-tested directly. The service below only loads the data and
/// delegates here.
/// </summary>
public static class RuleEvaluator
{
    private static bool IsWorkingType(string? type) =>
        string.Equals(type, ShiftTypes.Working, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase);

    /// <summary>ApprovedDenied values that mean "this Vacation is NOT an absence".</summary>
    private static readonly HashSet<string> DeniedVacationStates =
        new(StringComparer.OrdinalIgnoreCase) { "Denied", "Rejected", "Cancelled" };

    internal static bool IsAbsenceVacation(string? approvedDenied) =>
        string.IsNullOrWhiteSpace(approvedDenied) || !DeniedVacationStates.Contains(approvedDenied);

    private static string? NonParseableField(string? start, string? end)
    {
        if (!string.IsNullOrWhiteSpace(start) && !TimeSpan.TryParse(start, out _)) return "start";
        if (!string.IsNullOrWhiteSpace(end)   && !TimeSpan.TryParse(end,   out _)) return "end";
        return null;
    }

    private static bool ContainsAgent(string? agentIds, string employeeId)
    {
        if (string.IsNullOrWhiteSpace(agentIds)) return false;
        foreach (var raw in agentIds.Split(','))
            if (raw.Trim().Equals(employeeId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string DateStr(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static RuleWarning Warn(string code, string severity, DateOnly date,
        Dictionary<string, object?>? params_, string ref_, string message) =>
        new(code, severity, DateStr(date), params_ ?? new Dictionary<string, object?>(), ref_, message);

    public static List<RuleWarning> Evaluate(
        string employeeId,
        IEnumerable<ShiftEntry> shifts,
        IEnumerable<WicShiftEntry> wicEntries,
        IEnumerable<SickLeaveModel> sickLeaves,
        IEnumerable<Vacation> vacations,
        IEnumerable<TrainingSession> trainings,
        RulesOptions options,
        ProposedShift? proposed)
    {
        var warnings = new List<RuleWarning>();

        // Working shifts = WORKING / WIC_DUTY, plus the proposed shift when it belongs here.
        var working = shifts.Where(s => IsWorkingType(s.ShiftType)).ToList();
        if (proposed != null &&
            !string.IsNullOrWhiteSpace(proposed.EmployeeId) &&
            proposed.EmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase) &&
            DateOnly.TryParse(proposed.Date, out var pDate) &&
            IsWorkingType(proposed.ShiftType))
        {
            working.Add(new ShiftEntry
            {
                EmployeeId = employeeId,
                ShiftDate  = pDate,
                ShiftType  = proposed.ShiftType ?? ShiftTypes.Working,
                ShiftStart = proposed.Start,
                ShiftEnd   = proposed.End,
            });
        }

        // Build time windows; a present-but-unparseable time is DATA_INVALID, never a 500.
        var windows = new List<(DateOnly Date, DateTime Start, DateTime End, string? RawStart, string? RawEnd)>();
        foreach (var s in working)
        {
            var bad = NonParseableField(s.ShiftStart, s.ShiftEnd);
            if (bad != null)
            {
                warnings.Add(Warn(RuleCodes.DataInvalid, RuleSeverity.Warning, s.ShiftDate,
                    new Dictionary<string, object?> { ["field"] = bad }, "DATA",
                    $"Unparseable shift time ({bad})."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(s.ShiftStart) || string.IsNullOrWhiteSpace(s.ShiftEnd))
                continue; // no time recorded — nothing time-based to check

            var startTs = TimeSpan.Parse(s.ShiftStart);
            var endTs   = TimeSpan.Parse(s.ShiftEnd);
            var start   = s.ShiftDate.ToDateTime(TimeOnly.FromTimeSpan(startTs));
            var end     = s.ShiftDate.ToDateTime(TimeOnly.FromTimeSpan(endTs));
            if (endTs <= startTs) end = end.AddDays(1); // crosses midnight
            windows.Add((s.ShiftDate, start, end, s.ShiftStart, s.ShiftEnd));
        }

        // R1 — overlapping shifts of the same person (correct over midnight).
        for (int i = 0; i < windows.Count; i++)
            for (int j = i + 1; j < windows.Count; j++)
            {
                var a = windows[i];
                var b = windows[j];
                if (a.Start < b.End && b.Start < a.End)
                    warnings.Add(Warn(RuleCodes.Overlap, RuleSeverity.Error, a.Date,
                        new Dictionary<string, object?> { ["times"] = $"{b.RawStart}-{b.RawEnd}" },
                        "SO 325933, 4490553",
                        $"Overlapping shift on {DateStr(a.Date)}."));
            }

        // R5 — rest between consecutive (non-overlapping) shifts below MinRestHours.
        var ordered = windows.OrderBy(w => w.Start).ToList();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var rest = ordered[i + 1].Start - ordered[i].End;
            if (rest < TimeSpan.Zero) continue;      // overlap is R1's business
            if (rest.TotalHours < options.MinRestHours)
                warnings.Add(Warn(RuleCodes.MinRest, RuleSeverity.Error, ordered[i + 1].Date,
                    new Dictionary<string, object?> { ["hours"] = Math.Round(rest.TotalHours, 1) },
                    "ArbZG §5",
                    $"Only {Math.Round(rest.TotalHours, 1)}h rest between shifts (minimum {options.MinRestHours}h)."));
        }

        // R6 — more than MaxConsecutiveDays consecutive working days.
        var days = windows.Select(w => w.Date).Distinct().OrderBy(d => d).ToList();
        int maxRun = 0, run = 0;
        DateOnly runEnd = default;
        DateOnly? prevDay = null;
        foreach (var d in days)
        {
            run = (prevDay != null && d.DayNumber == prevDay.Value.DayNumber + 1) ? run + 1 : 1;
            if (run > maxRun) { maxRun = run; runEnd = d; }
            prevDay = d;
        }
        if (maxRun > options.MaxConsecutiveDays)
            warnings.Add(Warn(RuleCodes.Consecutive, RuleSeverity.Warning, runEnd,
                new Dictionary<string, object?> { ["days"] = maxRun },
                "ArbZG §5",
                $"{maxRun} consecutive working days (maximum {options.MaxConsecutiveDays})."));

        // R2 — shift during a recorded absence (SickLeaves, then Vacations).
        var sick = sickLeaves.ToList();
        var vac  = vacations.ToList();
        foreach (var s in working)
        {
            var d = s.ShiftDate;
            if (sick.Any(x => x.FirstDay <= d && x.LastDay >= d))
            {
                warnings.Add(Warn(RuleCodes.Absence, RuleSeverity.Error, d,
                    new Dictionary<string, object?> { ["type"] = "SL" }, "SO 325933",
                    $"Shift on {DateStr(d)} conflicts with recorded sick leave."));
                continue;
            }
            if (vac.Any(x => x.FirstDay <= d && x.LastDay >= d && IsAbsenceVacation(x.ApprovedDenied)))
            {
                warnings.Add(Warn(RuleCodes.Absence, RuleSeverity.Error, d,
                    new Dictionary<string, object?> { ["type"] = "AL" }, "SO 325933",
                    $"Shift on {DateStr(d)} conflicts with recorded annual leave."));
            }
        }

        // R3 — on-site WIC assignment at two or more different centres on the same day.
        foreach (var g in wicEntries.Where(w => w.IsOnSite).GroupBy(w => w.ShiftDate))
        {
            var locCount = g.Select(w => w.SupportLocation)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (locCount >= 2)
                warnings.Add(Warn(RuleCodes.OtherWic, RuleSeverity.Warning, g.Key,
                    new Dictionary<string, object?> { ["locations"] = locCount }, "WIC",
                    $"Assigned to {locCount} WIC centres on the same day."));
        }

        // R4 — shift during a training session the person is booked onto.
        var trainingDates = trainings
            .Where(t => ContainsAgent(t.AgentIds, employeeId))
            .Select(t => t.ScheduledDate)
            .ToHashSet();
        foreach (var s in working)
        {
            if (trainingDates.Contains(s.ShiftDate))
                warnings.Add(Warn(RuleCodes.Training, RuleSeverity.Warning, s.ShiftDate,
                    new Dictionary<string, object?>(), "Training",
                    $"Shift on {DateStr(s.ShiftDate)} overlaps a training session."));
        }

        return warnings;
    }
}

public class RulesService
{
    private readonly GSDContext _db;
    private readonly IOptionsMonitor<RulesOptions> _options;
    public RulesService(GSDContext db, IOptionsMonitor<RulesOptions> options)
    {
        _db = db;
        _options = options;
    }

    /// <summary>Loads the period's data and evaluates every rule for the affected people.</summary>
    private async Task<RulesResponse> EvaluateAsync(DateOnly from, DateOnly to, string? team, ProposedShift? proposed)
    {
        var opts = _options.CurrentValue;

        var empQ = _db.Employees.Where(e => e.IsActive);
        if (!string.IsNullOrWhiteSpace(team))
            empQ = empQ.Where(e => e.TeamLeadName == team);
        var ids = await empQ.Select(e => e.EmployeeId).Distinct().ToListAsync();

        if (!string.IsNullOrWhiteSpace(proposed?.EmployeeId) &&
            !ids.Contains(proposed.EmployeeId, StringComparer.OrdinalIgnoreCase))
            ids.Add(proposed.EmployeeId);

        if (ids.Count == 0)
            return new RulesResponse(DateStr(from), DateStr(to), new List<PersonWarnings>(),
                new RulesSummary(0, new Dictionary<string, int>()));

        // One query per source — no N+1.
        var shifts = await _db.ShiftEntries
            .Where(s => ids.Contains(s.EmployeeId) && s.ShiftDate >= from && s.ShiftDate <= to)
            .ToListAsync();
        var wic = await _db.WicShiftEntries
            .Where(w => ids.Contains(w.EmployeeId) && w.ShiftDate >= from && w.ShiftDate <= to && w.IsOnSite)
            .ToListAsync();
        var sick = await _db.SickLeaves
            .Where(s => s.EmployeeId != null && ids.Contains(s.EmployeeId) && s.FirstDay <= to && s.LastDay >= from)
            .ToListAsync();
        var vac = await _db.Vacations
            .Where(v => v.EmployeeId != null && ids.Contains(v.EmployeeId) && v.FirstDay <= to && v.LastDay >= from)
            .ToListAsync();
        var trainings = await _db.TrainingSessions
            .Where(t => t.ScheduledDate >= from && t.ScheduledDate <= to)
            .ToListAsync();

        var people = new List<PersonWarnings>();
        foreach (var id in ids)
        {
            var warnings = RuleEvaluator.Evaluate(
                id,
                shifts.Where(s => s.EmployeeId.Equals(id, StringComparison.OrdinalIgnoreCase)),
                wic.Where(w => w.EmployeeId.Equals(id, StringComparison.OrdinalIgnoreCase)),
                sick.Where(s => s.EmployeeId != null && s.EmployeeId.Equals(id, StringComparison.OrdinalIgnoreCase)),
                vac.Where(v => v.EmployeeId != null && v.EmployeeId.Equals(id, StringComparison.OrdinalIgnoreCase)),
                trainings, opts, proposed);
            if (warnings.Count == 0) continue;
            people.Add(new PersonWarnings(id, warnings));
        }

        var warningCounts = people.SelectMany(p => p.Warnings)
            .GroupBy(w => w.Code)
            .ToDictionary(g => g.Key, g => g.Count());
        return new RulesResponse(DateStr(from), DateStr(to), people, new RulesSummary(people.Count, warningCounts));
    }

    /// <summary>POST /api/rules/validate — period + optional proposed shift.</summary>
    public async Task<RulesResponse> ValidateAsync(DateOnly from, DateOnly to, string? team, ProposedShift? proposed)
    {
        var result = await EvaluateAsync(from, to, team, proposed);
        // A proposed shift narrows the answer to that one person.
        if (!string.IsNullOrWhiteSpace(proposed?.EmployeeId))
        {
            var person = result.People.FirstOrDefault(p =>
                p.EmployeeId.Equals(proposed.EmployeeId, StringComparison.OrdinalIgnoreCase));
            return new RulesResponse(result.From, result.To,
                person is null ? new List<PersonWarnings>() : new List<PersonWarnings> { person },
                person is null ? new RulesSummary(0, new Dictionary<string, int>())
                               : new RulesSummary(1, person.Warnings.GroupBy(w => w.Code).ToDictionary(g => g.Key, g => g.Count())));
        }
        return result;
    }

    /// <summary>GET /api/rules/week — every active person in the window.</summary>
    public Task<RulesResponse> ValidateWeekAsync(DateOnly from, DateOnly to, string? team) =>
        EvaluateAsync(from, to, team, proposed: null);

    private static string DateStr(DateOnly d) => d.ToString("yyyy-MM-dd");
}

public static class RulesEndpointMapper
{
    /// <summary>
    /// Same self-contained gate as ShiftService.CheckManualAccess: every new Rules
    /// endpoint requires a session AND a privileged role EVEN when
    /// Auth:EnforceAuthorization is false (that flag is a rollout gate for the shared
    /// middleware, not a reason for new surfaces to be anonymous). DEV/RTM/TEAM_LEAD
    /// pass; anonymous → 401; AGENT (or any other role) → 403.
    /// </summary>
    private static IResult? CheckRulesAccess(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
            return Results.Json(new { error = "Authentication required" }, statusCode: StatusCodes.Status401Unauthorized);
        var role = ctx.User.FindFirst(AuthClaims.Role)?.Value;
        if (role is AppRoles.Dev or AppRoles.Rtm or AppRoles.TeamLead)
            return null;
        return Results.Json(new { error = "Forbidden" }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static (DateOnly From, DateOnly To) Window(string? from, string? to)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var f = from != null && DateOnly.TryParse(from, out var pf) ? pf : today;
        var t = to   != null && DateOnly.TryParse(to,   out var pt) ? pt : f.AddDays(6);
        if (t < f) (f, t) = (t, f);
        return (f, t);
    }

    private static RulesResponse Disabled(string? from, string? to)
    {
        var (f, t) = Window(from, to);
        return new RulesResponse(f.ToString("yyyy-MM-dd"), t.ToString("yyyy-MM-dd"),
            new List<PersonWarnings>(), new RulesSummary(0, new Dictionary<string, int>()));
    }

    public static void MapRulesEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/rules").WithTags("Rules");

        grp.MapPost("/validate", async (RulesValidateRequest req, HttpContext ctx,
            RulesService svc, IOptionsMonitor<FeaturesOptions> features, ILoggerFactory loggerFactory) =>
        {
            var denied = CheckRulesAccess(ctx);
            if (denied != null) return denied;
            if (!features.CurrentValue.RulesEngine)
                return Results.Ok(Disabled(req.From, req.To));

            var sw = Stopwatch.StartNew();
            var (f, t) = Window(req.From, req.To);
            var result = await svc.ValidateAsync(f, t, req.Team, req.Proposed);
            sw.Stop();
            loggerFactory.CreateLogger("Rules").LogInformation(
                "Rules validate: duration={Duration}ms people={People} warnings={Warnings}",
                sw.ElapsedMilliseconds, result.People.Count,
                string.Join(",", result.Summary.Warnings.Select(kv => $"{kv.Key}={kv.Value}")));
            return Results.Ok(result);
        });

        grp.MapGet("/week", async (string? from, string? to, string? team, HttpContext ctx,
            RulesService svc, IOptionsMonitor<FeaturesOptions> features, ILoggerFactory loggerFactory) =>
        {
            var denied = CheckRulesAccess(ctx);
            if (denied != null) return denied;
            if (!features.CurrentValue.RulesEngine)
                return Results.Ok(Disabled(from, to));

            var sw = Stopwatch.StartNew();
            var (f, t) = Window(from, to);
            var result = await svc.ValidateWeekAsync(f, t, team);
            sw.Stop();
            loggerFactory.CreateLogger("Rules").LogInformation(
                "Rules week: duration={Duration}ms people={People} warnings={Warnings}",
                sw.ElapsedMilliseconds, result.People.Count,
                string.Join(",", result.Summary.Warnings.Select(kv => $"{kv.Key}={kv.Value}")));
            return Results.Ok(result);
        });
    }
}
