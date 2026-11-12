using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.WicShifts;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record BulkTimeRequest(
    string LocationCode,
    List<int>? ShiftIds = null,
    string? From = null,
    string? To = null,
    string Mode = "OPENING_HOURS",   // "OPENING_HOURS" | "FIXED"
    string? Start = null,            // FIXED: HH:mm
    string? End = null);             // FIXED: HH:mm

public record BulkTimeShiftResult(
    int Id, string EmployeeId, string Date,
    string? OldTime, string? NewTime, string? SkipReason);

public record BulkTimePreviewResponse(
    string LocationCode, string Mode, List<BulkTimeShiftResult> Results);

public record BulkTimeApplyResponse(
    string LocationCode, int Changed, int Skipped, List<BulkTimeShiftResult> Results);

// ── Service ───────────────────────────────────────────────────────────────────

public class WicBulkTimeService
{
    public const int MaxShiftsPerCall = 500;
    public const string ManualNote = "Bulk time adjust";

    private static readonly string[] AbsenceTasks = { "AL", "SL", "OFF" };
    private static readonly string[] AbsenceShiftTypes =
        { ShiftTypes.AnnualLeave, ShiftTypes.SickLeave, ShiftTypes.Off, ShiftTypes.OffWeekend };

    private readonly GSDContext _db;
    private readonly ILogger<WicBulkTimeService> _log;
    public WicBulkTimeService(GSDContext db, ILogger<WicBulkTimeService> log) { _db = db; _log = log; }

    /// <summary>Resolve the target on-site WicShiftEntries for a request, or an error string.</summary>
    private async Task<(List<WicShiftEntry>? Entries, string? Error)> ResolveEntriesAsync(BulkTimeRequest req)
    {
        var location = await _db.WicLocations.FirstOrDefaultAsync(l =>
            l.IsActive && (l.LocationCode == req.LocationCode || l.LocationCodeLegacy == req.LocationCode));
        if (location == null)
            return (null, "Location not found.");

        IQueryable<WicShiftEntry> q = _db.WicShiftEntries.Where(w => w.IsOnSite);

        if (req.ShiftIds is { Count: > 0 })
        {
            q = q.Where(w => req.ShiftIds.Contains(w.Id));
        }
        else
        {
            if (!DateOnly.TryParse(req.From, out var from) || !DateOnly.TryParse(req.To, out var to))
                return (null, "Invalid period: From/To must be yyyy-MM-dd.");
            if (from > to)
                return (null, "Invalid period: From must not be after To.");
            q = q.Where(w => w.ShiftDate >= from && w.ShiftDate <= to)
                 .Where(w => w.LocationCode == location.LocationCode
                          || w.LocationCode == location.LocationCodeLegacy
                          || w.SupportLocation == location.DisplayName);
        }

        var entries = await q.OrderBy(w => w.ShiftDate).ThenBy(w => w.EmployeeId).ToListAsync();
        if (entries.Count > MaxShiftsPerCall)
            return (null, $"Too many shifts: at most {MaxShiftsPerCall} per call (got {entries.Count}).");
        return (entries, null);
    }

    /// <summary>Compute the target "HH:mm-HH:mm" value, or null with a skip reason.</summary>
    private static (string? NewTime, string? NewStart, string? NewEnd, string? SkipReason) ComputeTarget(
        WicShiftEntry w, WicLocation loc, List<WicOpeningHour> allHours, BulkTimeRequest req)
    {
        var hours = WicHoursResolver.Resolve(allHours, loc.LocationCode, loc.LocationCodeLegacy,
            (int)w.ShiftDate.DayOfWeek, w.ShiftDate);
        var (dutyStart, dutyEnd) = WicHoursResolver.DutyWindow(hours);

        string? newStart, newEnd;
        if (string.Equals(req.Mode, "FIXED", StringComparison.OrdinalIgnoreCase))
        {
            newStart = req.Start?.Trim();
            newEnd = req.End?.Trim();
            if (string.IsNullOrWhiteSpace(newStart) || string.IsNullOrWhiteSpace(newEnd) ||
                !TimeOnly.TryParse(newStart, out var fs) || !TimeOnly.TryParse(newEnd, out var fe) || fe <= fs)
                return (null, null, null, "INVALID_FIXED_TIME");
            if (dutyStart == null || dutyEnd == null)
                return (null, null, null, "CLOSED_DAY");
            if (!TimeOnly.TryParse(dutyStart, out var ds) || !TimeOnly.TryParse(dutyEnd, out var de))
                return (null, null, null, "CLOSED_DAY");
            // The fixed window must sit inside the centre's duty window for that day.
            if (fs < ds || fe > de)
                return (null, null, null, "FIXED_OUTSIDE_HOURS");
        }
        else // OPENING_HOURS (default)
        {
            if (dutyStart == null || dutyEnd == null)
                return (null, null, null, "CLOSED_DAY");
            newStart = dutyStart;
            newEnd = dutyEnd;
        }

        var newTime = $"{newStart}-{newEnd}";
        if (string.Equals(Normalize(w.WorkingShift), newTime, StringComparison.OrdinalIgnoreCase))
            return (null, null, null, "UNCHANGED");
        return (newTime, newStart, newEnd, null);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Replace(" ", "");

    public async Task<(BulkTimePreviewResponse? Response, string? Error)> PreviewAsync(BulkTimeRequest req)
    {
        var (entries, error) = await ResolveEntriesAsync(req);
        if (error != null) return (null, error);
        if (entries == null) return (null, "Unknown error.");

        var loc = await _db.WicLocations.FirstAsync(l =>
            l.LocationCode == req.LocationCode || l.LocationCodeLegacy == req.LocationCode);
        var allHours = await _db.WicOpeningHours.ToListAsync();
        var empIds = entries.Select(e => e.EmployeeId).Distinct().ToList();
        var dates = entries.Select(e => e.ShiftDate).Distinct().ToList();
        var shiftTypes = await _db.ShiftEntries
            .Where(s => empIds.Contains(s.EmployeeId) && dates.Contains(s.ShiftDate))
            .Select(s => new { s.EmployeeId, s.ShiftDate, s.ShiftType })
            .ToListAsync();

        var results = new List<BulkTimeShiftResult>();
        foreach (var w in entries)
        {
            var (newTime, newStart, newEnd, skip) = ComputeTarget(w, loc, allHours, req);
            string? skipReason = skip;
            if (skipReason == null && AbsenceTasks.Contains(w.Task))
                skipReason = "ABSENCE";
            if (skipReason == null)
            {
                var st = shiftTypes.FirstOrDefault(x => x.EmployeeId == w.EmployeeId && x.ShiftDate == w.ShiftDate)?.ShiftType;
                if (st != null && AbsenceShiftTypes.Contains(st))
                    skipReason = "ABSENCE";
            }

            results.Add(new BulkTimeShiftResult(
                w.Id, w.EmployeeId, w.ShiftDate.ToString("yyyy-MM-dd"),
                w.WorkingShift, skipReason == null ? newTime : null, skipReason));
        }
        return (new BulkTimePreviewResponse(req.LocationCode, req.Mode, results), null);
    }

    public async Task<(BulkTimeApplyResponse? Response, string? Error)> ApplyAsync(BulkTimeRequest req, string actor)
    {
        var (entries, error) = await ResolveEntriesAsync(req);
        if (error != null) return (null, error);
        if (entries == null) return (null, "Unknown error.");

        var loc = await _db.WicLocations.FirstAsync(l =>
            l.LocationCode == req.LocationCode || l.LocationCodeLegacy == req.LocationCode);
        var allHours = await _db.WicOpeningHours.ToListAsync();

        var strategy = _db.Database.CreateExecutionStrategy();
        var response = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var results = new List<BulkTimeShiftResult>();
            int changed = 0, skipped = 0;

            // Load all ShiftEntries for the affected employees+dates in one query, and all
            // sibling on-site rows for the multi-centre widening rule.
            var empIds = entries.Select(e => e.EmployeeId).Distinct().ToList();
            var dates = entries.Select(e => e.ShiftDate).Distinct().ToList();
            var shiftEntries = await _db.ShiftEntries
                .Where(s => empIds.Contains(s.EmployeeId) && dates.Contains(s.ShiftDate))
                .ToListAsync();
            var siblingRows = await _db.WicShiftEntries
                .Where(w => w.IsOnSite && empIds.Contains(w.EmployeeId) && dates.Contains(w.ShiftDate))
                .ToListAsync();

            foreach (var w in entries)
            {
                var (newTime, newStart, newEnd, skip) = ComputeTarget(w, loc, allHours, req);
                string? skipReason = skip;
                if (skipReason == null && AbsenceTasks.Contains(w.Task))
                    skipReason = "ABSENCE";

                var shiftEntry = shiftEntries.FirstOrDefault(s =>
                    s.EmployeeId == w.EmployeeId && s.ShiftDate == w.ShiftDate);
                if (skipReason == null && shiftEntry != null && AbsenceShiftTypes.Contains(shiftEntry.ShiftType ?? ""))
                    skipReason = "ABSENCE";

                if (skipReason != null)
                {
                    skipped++;
                    results.Add(new BulkTimeShiftResult(w.Id, w.EmployeeId,
                        w.ShiftDate.ToString("yyyy-MM-dd"), w.WorkingShift, null, skipReason));
                    continue;
                }

                // Same field set a single-shift edit touches: WicShiftEntries.WorkingShift +
                // ShiftEntries.ShiftStart/ShiftEnd (+ ManualChangeBy/At/Note), widened for the
                // multi-centre rule so an edit for one centre never narrows another centre's span.
                w.WorkingShift = newTime!;
                if (shiftEntry != null)
                {
                    var otherParts = siblingRows
                        .Where(x => x.EmployeeId == w.EmployeeId && x.ShiftDate == w.ShiftDate && x.Id != w.Id)
                        .Select(x => x.WorkingShift)
                        .Append(newTime)
                        .ToList();
                    if (WicWorkingShift.WidenDaily(shiftEntry.ShiftStart, shiftEntry.ShiftEnd, otherParts) is { } widened)
                    {
                        shiftEntry.ShiftStart = widened.Start;
                        shiftEntry.ShiftEnd = widened.End;
                    }
                    else
                    {
                        shiftEntry.ShiftStart = newStart;
                        shiftEntry.ShiftEnd = newEnd;
                    }
                    _db.RecordManualShiftEdit(shiftEntry, actor);
                    shiftEntry.ManualChangeNote = ManualNote;
                }

                changed++;
                results.Add(new BulkTimeShiftResult(w.Id, w.EmployeeId,
                    w.ShiftDate.ToString("yyyy-MM-dd"), w.WorkingShift, newTime, null));
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _log.LogInformation("Bulk time apply {Location}: {Changed} changed, {Skipped} skipped by {Actor}",
                req.LocationCode, changed, skipped, actor);

            return new BulkTimeApplyResponse(req.LocationCode, changed, skipped, results);
        });
        return (response, null);
    }
}

// ── Endpoints ─────────────────────────────────────────────────────────────────

public static class WicBulkTimeEndpointMapper
{
    public static void MapWicBulkTimeEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/wic-shifts/bulk-time").WithTags("WIC");

        // Same always-on session/role gate as the single-shift edit endpoints
        // (ShiftEndpointMapper.CheckManualAccess / WicEndpointMapper.CheckAssignAnywayAccess):
        // anonymous -> 401, non DEV/RTM/TEAM_LEAD -> 403.
        grp.MapPost("/preview", async (BulkTimeRequest req, WicBulkTimeService svc, HttpContext ctx) =>
        {
            var denied = WicEndpointMapper.CheckAssignAnywayAccess(ctx);
            if (denied != null) return denied;
            var (response, error) = await svc.PreviewAsync(req);
            if (error != null) return Results.BadRequest(new { error });
            return Results.Ok(response);
        });

        grp.MapPost("/apply", async (BulkTimeRequest req, WicBulkTimeService svc, HttpContext ctx) =>
        {
            var denied = WicEndpointMapper.CheckAssignAnywayAccess(ctx);
            if (denied != null) return denied;
            var actor = ctx.User.FindFirst(AuthClaims.Kid)!.Value;
            var (response, error) = await svc.ApplyAsync(req, actor);
            if (error != null) return Results.BadRequest(new { error });
            return Results.Ok(response);
        });
    }
}
