using System.Text.Json;
using GSDDashboard.API.Data;
using GSDDashboard.API.Modules.SickLeave;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GSDDashboard.API.Services;

/// <summary>
/// Background sync that turns a kiosk SICK_LEAVE report into the Pulse SickLeaves
/// register, so "sick leave is one thing wherever it is entered" (owner decision).
///
/// It reads the kiosk attendance feed through the existing <see cref="IKioskProxy"/>
/// (no kiosk server change) once per tick, for the Berlin "today" only:
///   * an agent whose LATEST today kiosk status is SICK_LEAVE gets a sick leave for
///     today — extend a spell that ended the previous workday (PatchAsync), or create
///     a new single-day record (CreateAsync);
///   * an agent whose latest today status is ON_SHIFT deletes the KIOSK-created
///     single-day leave (DeleteAsync) — they came back to work after a sick report.
///
/// Every write goes through <see cref="SickLeaveService"/> with actor "KIOSK": the
/// same atomic path the Shift Plan SL sync uses (CreateAsync/PatchAsync/DeleteAsync
/// are transactional and keep ShiftEntries in step via ShiftSyncService).
///
/// Agent → EmployeeId mapping is the kiosk feed's own employee_id field; a row
/// without one is skipped and logged (never guessed).
/// </summary>
public sealed class KioskSickLeaveSyncService : BackgroundService
{
    // Sick-leave spell types (same set as SickLeaveService; "SL" is the legacy
    // spelling of "Self"). A List, not a HashSet, so EF Core translates Contains
    // into a SQL IN clause. Other leave types that happen to live in the SickLeaves
    // table (OFF/OL/UL/CD) are deliberately not sick leave and never match.
    private static readonly List<string> SickTypes = new() { "Self", "SL", "Child" };

    private const string SourceSheet = "KIOSK";
    private const string Comments   = "Reported in kiosk";
    private const string Actor      = "KIOSK";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<KioskSickLeaveSyncService> _logger;

    public KioskSickLeaveSyncService(IServiceScopeFactory scopeFactory, IConfiguration config,
        ILogger<KioskSickLeaveSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First pass runs immediately at startup, then every 5 minutes. A pass must
        // never bring the host down, so every failure is caught and logged.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KioskSickLeaveSync pass failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        // Switch: Kiosk:SickLeaveSync:Enabled (default true). When false, do nothing.
        if (!_config.GetValue<bool>("Kiosk:SickLeaveSync:Enabled", defaultValue: true))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var kiosk = scope.ServiceProvider.GetRequiredService<IKioskProxy>();
        if (!kiosk.IsConfigured)
        {
            _logger.LogWarning("KioskSickLeaveSync skipped: Kiosk:ServiceToken is not configured");
            return;
        }

        var today = BerlinTime.Today;
        var (status, body) = await kiosk.GetAttendanceAsync(today.ToString("yyyy-MM-dd"), null, ct);
        if (status != 200)
        {
            _logger.LogWarning("KioskSickLeaveSync skipped: kiosk attendance returned HTTP {Status}", status);
            return;
        }

        var rows = ParseAttendance(body);
        if (rows.Count == 0)
        {
            _logger.LogInformation("KioskSickLeaveSync pass: no kiosk attendance rows for {Date}", today);
            return;
        }

        var sickRows   = rows.Where(r => r.Status == "SICK_LEAVE").ToList();
        var onShiftRows = rows.Where(r => r.Status == "ON_SHIFT").ToList();
        if (sickRows.Count == 0 && onShiftRows.Count == 0)
        {
            _logger.LogInformation("KioskSickLeaveSync pass: no SICK_LEAVE/ON_SHIFT rows for {Date}", today);
            return;
        }

        var sickService = scope.ServiceProvider.GetRequiredService<SickLeaveService>();
        var db = scope.ServiceProvider.GetRequiredService<GSDContext>();

        int processed = 0, created = 0, extended = 0, deleted = 0,
            skippedAlreadyCovered = 0, skippedNoMapping = 0, warnings = 0;

        // ── Part A: SICK_LEAVE → ensure a sick leave covers today ──────────────
        foreach (var row in sickRows)
        {
            if (string.IsNullOrWhiteSpace(row.EmployeeId))
            {
                skippedNoMapping++;
                _logger.LogWarning("KioskSickLeaveSync: SICK_LEAVE row without employee_id (agent {AgentId}) skipped — no mapping",
                    row.AgentId ?? "?");
                continue;
            }
            var empId = row.EmployeeId;

            var covered = await db.SickLeaves.AsNoTracking().AnyAsync(s =>
                s.EmployeeId == empId && s.FirstDay <= today && s.LastDay >= today
                && s.LeaveType != null && SickTypes.Contains(s.LeaveType), ct);
            if (covered)
            {
                skippedAlreadyCovered++;
                continue; // already covered — nothing to do
            }

            var prev = PreviousWorkday(today);
            var before = await db.SickLeaves.AsNoTracking().FirstOrDefaultAsync(s =>
                s.EmployeeId == empId && s.LastDay == prev
                && s.LeaveType != null && SickTypes.Contains(s.LeaveType), ct);

            if (before != null)
            {
                // Extend a spell that ended the previous workday onto today.
                var patch = await sickService.PatchAsync(before.Id,
                    new PatchSickLeaveRequest(null, today.ToString("yyyy-MM-dd"), null, null), Actor);
                if (patch.Error != null)
                {
                    warnings++;
                    _logger.LogWarning("KioskSickLeaveSync: extend failed for {EmployeeId} (sick leave {Id}): {Error}",
                        empId, before.Id, patch.Error);
                }
                else extended++;
            }
            else
            {
                // New single-day sick leave.
                var create = await sickService.CreateAsync(new CreateSickLeaveRequest(
                    empId, null, null,
                    today.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"),
                    "Self", null, Comments), Actor, SourceSheet);
                if (create.Error != null)
                {
                    warnings++;
                    _logger.LogWarning("KioskSickLeaveSync: create failed for {EmployeeId}: {Error}", empId, create.Error);
                }
                else created++;
            }
            processed++;
        }

        // ── Part B: ON_SHIFT after SICK_LEAVE → delete the KIOSK single-day leave ──
        foreach (var row in onShiftRows)
        {
            if (string.IsNullOrWhiteSpace(row.EmployeeId)) continue;
            var empId = row.EmployeeId;

            var kioskLeave = await db.SickLeaves.AsNoTracking().FirstOrDefaultAsync(s =>
                s.EmployeeId == empId && s.SourceSheet == SourceSheet
                && s.FirstDay <= today && s.LastDay >= today, ct);
            if (kioskLeave == null) continue; // nothing KIOSK created for today

            if (kioskLeave.FirstDay != today || kioskLeave.LastDay != today)
            {
                // Spans more than the single KIOSK day (extended by RTM or the sync) — leave it.
                warnings++;
                _logger.LogWarning("KioskSickLeaveSync: KIOSK sick leave {Id} for {EmployeeId} spans more than today — not deleted",
                    kioskLeave.Id, empId);
                continue;
            }
            if (!string.Equals(kioskLeave.LeaveType, "Self", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(kioskLeave.Comments, Comments, StringComparison.OrdinalIgnoreCase))
            {
                // The KIOSK fingerprint changed (RTM edited type/notes) — leave it.
                warnings++;
                _logger.LogWarning("KioskSickLeaveSync: KIOSK sick leave {Id} for {EmployeeId} was changed by RTM — not deleted",
                    kioskLeave.Id, empId);
                continue;
            }
            await sickService.DeleteAsync(kioskLeave.Id, Actor);
            deleted++;
        }

        _logger.LogInformation(
            "KioskSickLeaveSync pass complete: processed={Processed} created={Created} extended={Extended} deleted={Deleted} " +
            "skippedAlreadyCovered={SkippedAlreadyCovered} skippedNoMapping={SkippedNoMapping} warnings={Warnings} errors=0",
            processed, created, extended, deleted, skippedAlreadyCovered, skippedNoMapping, warnings);
    }

    private static List<AttendanceRow> ParseAttendance(string body)
    {
        var result = new List<AttendanceRow>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var status = el.TryGetProperty("current_status", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString() : null;
                var employeeId = el.TryGetProperty("employee_id", out var e) ? ReadEmployeeId(e) : null;
                var agentId = el.TryGetProperty("agent_id", out var a) && a.ValueKind != JsonValueKind.Null
                    ? a.ToString() : null;
                result.Add(new AttendanceRow(employeeId, status, agentId));
            }
        }
        catch (JsonException)
        {
            // KioskProxy already validates the 200 body is a well-formed array; a
            // defensive empty result is enough here.
        }
        return result;
    }

    private static string? ReadEmployeeId(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        _ => null,
    };

    private static bool IsWeekend(DateOnly d) =>
        d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateOnly PreviousWorkday(DateOnly d)
    {
        d = d.AddDays(-1);
        while (IsWeekend(d)) d = d.AddDays(-1);
        return d;
    }

    private sealed record AttendanceRow(string? EmployeeId, string? Status, string? AgentId);
}
