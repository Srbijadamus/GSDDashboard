using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GSDDashboard.API.Modules.SubstituteAccept;

public record AcceptSubstituteRequest(
    string EmployeeId,
    string LocationCode,
    string Date,
    string? ShiftStart,
    string? ShiftEnd,
    string? SourceType
);

public static class SubstituteAcceptEndpointMapper
{
    public static void MapSubstituteAcceptEndpoints(this WebApplication app)
    {
        app.MapPost("/api/wic/substitutes/accept", async (
            AcceptSubstituteRequest req,
            GSDContext db,
            ILoggerFactory loggerFactory) =>
        {
            if (string.IsNullOrEmpty(req.EmployeeId) || string.IsNullOrEmpty(req.LocationCode))
                return Results.BadRequest(new { error = "EmployeeId and LocationCode are required." });

            if (!DateOnly.TryParse(req.Date, out var date))
                return Results.BadRequest(new { error = "Invalid date format. Expected yyyy-MM-dd." });

            var location = await db.WicLocations
                .FirstOrDefaultAsync(l => l.LocationCode == req.LocationCode && l.IsActive);
            if (location == null)
                return Results.NotFound(new { error = $"Location '{req.LocationCode}' not found." });

            // Look up opening hours to populate WorkingShift on the WicShiftEntry
            int dow = (int)date.DayOfWeek;
            var allHoursForSub = await db.WicOpeningHours.ToListAsync();
            var hours = WicHoursResolver.Resolve(allHoursForSub, req.LocationCode, dow, date);
            string openTime  = hours?.OpenTime  ?? req.ShiftStart ?? "08:00";
            string closeTime = hours?.CloseTime ?? req.ShiftEnd   ?? "17:00";
            string workingShift = $"{openTime}-{closeTime}";

            var employee = await db.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId && e.IsActive);
            if (employee == null)
                return Results.NotFound(new { error = $"Employee '{req.EmployeeId}' not found." });

            // AgentTask column is MaxLength(20) — truncate display name if needed
            var agentTask = (location.DisplayName?.Length ?? 0) > 20
                ? location.DisplayName![..20]
                : location.DisplayName ?? req.LocationCode[..Math.Min(20, req.LocationCode.Length)];

            var logger = loggerFactory.CreateLogger("SubstituteAccept");

            // Every ShiftEntries row this agent holds on the date. Normally one; a second
            // row with a different ShiftType is possible (UQ_ShiftEntries_EmpDateType allows
            // WORKING + WIC_DUTY on the same day) and is exactly the shape that used to
            // surface as the generic "concurrent change" message.
            var dayShifts = await db.ShiftEntries
                .Where(s => s.EmployeeId == req.EmployeeId && s.ShiftDate == date)
                .ToListAsync();
            var existingShift = dayShifts.FirstOrDefault();

            // A human's manual cell (e.g. a deliberate "move to GSD") is never overwritten
            // by this automatic writer. State exactly who is blocked and by which duty: the
            // old "Manual shift is protected; edit the cell directly." did not, and the
            // Overview drawer replaced it with a generic failure anyway (live 2026-10-12,
            // ShiftEntries id 56515, employee 9074345, WORKING/GSD, ManualChangeBy S60028).
            if (db.PreserveManualShift(existingShift, "Substitute accept"))
            {
                var duty = string.IsNullOrWhiteSpace(existingShift!.AgentTask)
                    ? existingShift.ShiftType
                    : existingShift.AgentTask!;
                return Results.Conflict(new
                {
                    code = "MANUAL_ENTRY_PROTECTED",
                    error = $"{employee.FullName ?? req.EmployeeId} already has a duty today: {duty}. It is a protected manual entry - edit the cell directly instead.",
                    details = new { name = employee.FullName ?? req.EmployeeId, duty, date = date.ToString("yyyy-MM-dd") }
                });
            }

            // A second WIC_DUTY row for the same agent and day would collide with the row
            // about to be changed. Name it instead of letting SaveChanges throw.
            var otherWicDuty = dayShifts.FirstOrDefault(s => !ReferenceEquals(s, existingShift)
                && string.Equals(s.ShiftType, ShiftTypes.WicDuty, StringComparison.OrdinalIgnoreCase));
            if (otherWicDuty != null)
            {
                var duty = string.IsNullOrWhiteSpace(otherWicDuty.AgentTask)
                    ? otherWicDuty.ShiftType
                    : otherWicDuty.AgentTask!;
                return Results.Conflict(new
                {
                    code = "ALREADY_HAS_WIC_DUTY",
                    error = $"{employee.FullName ?? req.EmployeeId} already has a WIC duty today: {duty}.",
                    details = new { name = employee.FullName ?? req.EmployeeId, duty, date = date.ToString("yyyy-MM-dd") }
                });
            }

            IResult? errorResult = null;
            string? empName = employee.FullName ?? req.EmployeeId;
            string? locName = location.DisplayName;

            async Task ApplyAsync()
            {
                // Update existing ShiftEntry for the date, or create a new one
                var shift = await db.ShiftEntries
                    .FirstOrDefaultAsync(s => s.EmployeeId == req.EmployeeId && s.ShiftDate == date);

                if (shift != null)
                {
                    shift.ShiftType    = ShiftTypes.WicDuty;
                    shift.IsWicDuty    = true;
                    shift.AgentTask    = agentTask;
                    shift.SourceModule = "SUBSTITUTE";
                    if (req.ShiftStart != null) shift.ShiftStart = req.ShiftStart;
                    if (req.ShiftEnd   != null) shift.ShiftEnd   = req.ShiftEnd;
                }
                else
                {
                    db.ShiftEntries.Add(new ShiftEntry
                    {
                        EmployeeId    = req.EmployeeId,
                        ShiftDate     = date,
                        ShiftType     = ShiftTypes.WicDuty,
                        IsWicDuty     = true,
                        AgentTask     = agentTask,
                        ShiftStart    = req.ShiftStart,
                        ShiftEnd      = req.ShiftEnd,
                        SourceSheet   = "SUBSTITUTE",
                        SourceModule  = "SUBSTITUTE",
                        AutoGenerated = true
                    });
                }

                // Upsert WicShiftEntry — remove stale rows (NULL SupportLocation) then upsert the target row.
                var allWicRows = await db.WicShiftEntries
                    .Where(w => w.EmployeeId == req.EmployeeId && w.ShiftDate == date)
                    .ToListAsync();
                var targetRow = allWicRows.FirstOrDefault(w =>
                    string.Equals(w.SupportLocation, location.DisplayName, StringComparison.OrdinalIgnoreCase));
                var staleRows = allWicRows.Where(w => w != targetRow).ToList();
                db.WicShiftEntries.RemoveRange(staleRows);

                if (targetRow != null)
                {
                    targetRow.IsOnSite     = true;
                    targetRow.IsOffDay     = false;
                    targetRow.IsGSDDay     = false;
                    targetRow.WorkingShift = workingShift;
                    targetRow.Task         = "WIC";
                }
                else
                {
                    db.WicShiftEntries.Add(new WicShiftEntry
                    {
                        EmployeeId      = req.EmployeeId,
                        ShiftDate       = date,
                        DayOfWeek       = date.DayOfWeek.ToString(),
                        SupportLocation = location.DisplayName,
                        IsOnSite        = true,
                        IsGSDDay        = false,
                        IsOffDay        = false,
                        WorkingShift    = workingShift,
                        Task            = "WIC",
                    });
                }

                db.SubstitutionHistory.Add(new SubstitutionHistory
                {
                    EmployeeId   = req.EmployeeId,
                    LocationCode = req.LocationCode,
                    Date         = date,
                    SourceType   = req.SourceType,
                    AssignedAt   = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
            }

            void RecordFailure(Exception ex)
            {
                logger.LogError(ex, "SubstituteAccept failed for {EmployeeId} at {LocationCode} on {Date}",
                    req.EmployeeId, req.LocationCode, req.Date);
                // Name the real reason instead of a bare "a concurrent change may have
                // occurred": the frontend shows this text, and a swallowed cause is exactly
                // what made the live failure look like a mystery.
                errorResult = Results.Conflict(new
                {
                    code = "ACCEPT_FAILED",
                    error = $"Accept substitute failed: {ex.Message}",
                    detail = ex.Message
                });
            }

            // The in-memory test provider has no transactions; real SQL Server keeps the
            // all-or-nothing guarantee this route always had (same split as MoveToGsdAsync).
            if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                try { await ApplyAsync(); }
                catch (Exception ex) { RecordFailure(ex); }
            }
            else
            {
                var strategy = db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    try
                    {
                        await ApplyAsync();
                        await tx.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        await tx.RollbackAsync();
                        RecordFailure(ex);
                    }
                });
            }

            if (errorResult != null) return errorResult;

            return Results.Ok(new
            {
                success      = true,
                employeeName = empName,
                displayName  = locName,
                date         = date.ToString("yyyy-MM-dd")
            });
        }).WithTags("WIC");
    }
}
