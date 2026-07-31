using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Shifts;

public record ShiftRowDto(
    int Id,
    string EmployeeId,
    string? FullName,
    string? Engagement,
    string? PrimaryRole,
    string? SecondaryRole,
    string? TeamLeadName,
    DateOnly ShiftDate,
    string ShiftType,
    string? ShiftStart,
    string? ShiftEnd,
    bool IsWicDuty,
    string? RawValue,
    string? AgentTask,
    string? LocationId,
    string? AssignmentStatus
);

public record ShiftFilterParams(
    string? From,
    string? To,
    string? TeamLead,
    string? Role,
    string? Engagement,
    string? ShiftType
);

public record ShiftValidateRequest(int ShiftId, string ShiftType, string? ShiftStart, string? ShiftEnd);
public record ShiftUpdateDto(
    string? ShiftType,
    string? ShiftStart,
    string? ShiftEnd,
    string? AgentTask,
    string? LocationId,
    string? AssignmentStatus
);
public record AssignShiftDto(string EmployeeId, string ShiftDate, string ShiftType, string? ShiftStart, string? ShiftEnd);
public record AssignShiftResult(ShiftRowDto? Row, string? DuplicateError);
public record SwapShiftDto(string NewEmployeeId);

public class ShiftService
{
    private readonly GSDContext _db;
    public ShiftService(GSDContext db) => _db = db;

    public async Task<List<ShiftRowDto>> GetShiftsAsync(ShiftFilterParams f)
    {
        var from = f.From != null && DateOnly.TryParse(f.From, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
        var to   = f.To   != null && DateOnly.TryParse(f.To,   out var td) ? td : from;

        var query = _db.ShiftEntries
            .Where(s => s.ShiftDate >= from && s.ShiftDate <= to)
            .Join(_db.Employees,
                  s => s.EmployeeId,
                  e => e.EmployeeId,
                  (s, e) => new { Shift = s, Employee = e })
            .Where(x => x.Employee.IsActive);

        if (!string.IsNullOrWhiteSpace(f.TeamLead))
            query = query.Where(x => x.Employee.TeamLeadName == f.TeamLead);
        if (!string.IsNullOrWhiteSpace(f.Role))
            query = query.Where(x => x.Employee.PrimaryRole == f.Role);
        if (!string.IsNullOrWhiteSpace(f.Engagement))
            query = query.Where(x => x.Employee.Engagement == f.Engagement);
        if (!string.IsNullOrWhiteSpace(f.ShiftType))
            query = query.Where(x => x.Shift.ShiftType == f.ShiftType);

        var rows = await query
            .OrderBy(x => x.Employee.TeamLeadName)
            .ThenBy(x => x.Employee.FullName)
            .ThenBy(x => x.Shift.ShiftDate)
            .ToListAsync();

        return rows.Select(x => new ShiftRowDto(
            x.Shift.Id,
            x.Employee.EmployeeId,
            x.Employee.FullName ?? x.Employee.EmployeeId,
            x.Employee.Engagement,
            x.Employee.PrimaryRole,
            x.Employee.SecondaryRole,
            x.Employee.TeamLeadName,
            x.Shift.ShiftDate,
            x.Shift.ShiftType,
            x.Shift.ShiftStart,
            x.Shift.ShiftEnd,
            x.Shift.IsWicDuty,
            x.Shift.RawValue,
            x.Shift.AgentTask,
            x.Shift.LocationId,
            x.Shift.AssignmentStatus
        )).ToList();
    }

    public async Task<List<ShiftRowDto>> GetWorkingTodayAsync(DateOnly date)
    {
        var rows = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date && s.ShiftType == ShiftTypes.Working)
            .Join(_db.Employees,
                  s => s.EmployeeId,
                  e => e.EmployeeId,
                  (s, e) => new { Shift = s, Employee = e })
            .Where(x => x.Employee.IsActive)
            .OrderBy(x => x.Employee.PrimaryRole)
            .ThenBy(x => x.Employee.FullName)
            .ToListAsync();

        return rows.Select(x => new ShiftRowDto(
            x.Shift.Id,
            x.Employee.EmployeeId,
            x.Employee.FullName ?? x.Employee.EmployeeId,
            x.Employee.Engagement,
            x.Employee.PrimaryRole,
            x.Employee.SecondaryRole,
            x.Employee.TeamLeadName,
            x.Shift.ShiftDate,
            x.Shift.ShiftType,
            x.Shift.ShiftStart,
            x.Shift.ShiftEnd,
            x.Shift.IsWicDuty,
            x.Shift.RawValue,
            x.Shift.AgentTask,
            x.Shift.LocationId,
            x.Shift.AssignmentStatus
        )).ToList();
    }

    public async Task<ShiftRowDto?> UpdateShiftAsync(int id, ShiftUpdateDto dto)
    {
        var shift = await _db.ShiftEntries.FindAsync(id);
        if (shift == null) return null;

        if (dto.ShiftType        != null) shift.ShiftType        = dto.ShiftType;
        if (dto.ShiftStart       != null) shift.ShiftStart       = dto.ShiftStart;
        if (dto.ShiftEnd         != null) shift.ShiftEnd         = dto.ShiftEnd;
        if (dto.AgentTask        != null) shift.AgentTask        = dto.AgentTask;
        if (dto.LocationId       != null) shift.LocationId       = dto.LocationId;
        if (dto.AssignmentStatus != null) shift.AssignmentStatus = dto.AssignmentStatus;

        await _db.SaveChangesAsync();

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == shift.EmployeeId);
        return new ShiftRowDto(
            shift.Id,
            shift.EmployeeId,
            emp?.FullName ?? shift.EmployeeId,
            emp?.Engagement, emp?.PrimaryRole, emp?.SecondaryRole, emp?.TeamLeadName,
            shift.ShiftDate, shift.ShiftType, shift.ShiftStart, shift.ShiftEnd,
            shift.IsWicDuty, shift.RawValue,
            shift.AgentTask, shift.LocationId, shift.AssignmentStatus
        );
    }

    // Assigns a shift type for employee+date. Rejects with a DuplicateError when the
    // exact same (employee, date, type) already exists. A different type on the same day
    // is allowed — the existing row is updated (e.g. EMPTY → AL, or WORKING → SL).
    // The DB enforces one row per (employee, date) via UQ_ShiftEntries_EmpDate.
    public async Task<AssignShiftResult> AssignShiftAsync(AssignShiftDto dto)
    {
        if (!DateOnly.TryParse(dto.ShiftDate, out var date)) return new(null, null);
        if (date > DateOnly.FromDateTime(DateTime.Today).AddDays(ScheduleLimits.MaxFutureDays)) return new(null, null);

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId && e.IsActive);
        if (emp == null) return new(null, null);

        var existing = await _db.ShiftEntries.FirstOrDefaultAsync(
            s => s.EmployeeId == dto.EmployeeId && s.ShiftDate == date);

        if (existing != null && existing.ShiftType == dto.ShiftType)
        {
            var name = emp.FullName ?? dto.EmployeeId;
            return new(null, $"Already exists: {name} on {date:yyyy-MM-dd} ({dto.ShiftType})");
        }

        ShiftEntry shift;
        if (existing != null)
        {
            shift = existing;
            shift.ShiftType  = dto.ShiftType;
            shift.ShiftStart = dto.ShiftStart;
            shift.ShiftEnd   = dto.ShiftEnd;
        }
        else
        {
            shift = new ShiftEntry
            {
                EmployeeId = dto.EmployeeId,
                ShiftDate  = date,
                ShiftType  = dto.ShiftType,
                ShiftStart = dto.ShiftStart,
                ShiftEnd   = dto.ShiftEnd,
            };
            _db.ShiftEntries.Add(shift);
        }
        await _db.SaveChangesAsync();

        return new(new ShiftRowDto(
            shift.Id, emp.EmployeeId, emp.FullName ?? emp.EmployeeId,
            emp.Engagement, emp.PrimaryRole, emp.SecondaryRole, emp.TeamLeadName,
            shift.ShiftDate, shift.ShiftType, shift.ShiftStart, shift.ShiftEnd,
            shift.IsWicDuty, shift.RawValue, shift.AgentTask, shift.LocationId, shift.AssignmentStatus
        ), null);
    }

    public record CoverageSlot(string Hour, int Voice, int Wic, int Al, int Sick, int Training, int Off, bool BelowThreshold, int MinRequired);
public record CoverageResponse(string Date, List<CoverageSlot> Slots, int Threshold);

    public async Task<CoverageResponse> GetCoverageAsync(DateOnly date)
    {
        // Per-hour minimum: <08:00 → 1,  08:00–17:00 → 3,  ≥17:00 → 1
        static int MinRequired(int h) => h < 8 ? 1 : h < 17 ? 3 : 1;

        var shifts = await _db.ShiftEntries
            .Where(s => s.ShiftDate == date)
            .Join(_db.Employees, s => s.EmployeeId, e => e.EmployeeId, (s, e) => new { Shift = s, Employee = e })
            .Where(x => x.Employee.IsActive && x.Employee.PrimaryRole != "2nd Level")
            .ToListAsync();

        var slots = new List<CoverageSlot>();
        for (int h = 7; h <= 18; h++)
        {
            var hour = $"{h:D2}:00";
            var timeSpan = new TimeSpan(h, 0, 0);
            int voice = 0, wic = 0, al = 0, sick = 0, training = 0, off = 0;

            foreach (var x in shifts)
            {
                var s = x.Shift;
                var st = s.ShiftType;
                if (st == ShiftTypes.AnnualLeave || st == ShiftTypes.HalfAL) { al++; continue; }
                if (st == ShiftTypes.SickLeave) { sick++; continue; }
                if (st == ShiftTypes.Training) { training++; continue; }
                if (st == ShiftTypes.Off || st == ShiftTypes.OffWeekend || st == ShiftTypes.PublicHol) { off++; continue; }
                if (st == ShiftTypes.Working || st == ShiftTypes.WicDuty)
                {
                    if (s.ShiftStart == null || s.ShiftEnd == null) continue;
                    if (!TimeSpan.TryParse(s.ShiftStart, out var start) || !TimeSpan.TryParse(s.ShiftEnd, out var end)) continue;
                    if (timeSpan >= start && timeSpan < end)
                    {
                        if (st == ShiftTypes.WicDuty || s.IsWicDuty) wic++;
                        else voice++;
                    }
                }
            }
            int min = MinRequired(h);
            slots.Add(new CoverageSlot(hour, voice, wic, al, sick, training, off, (voice + wic) < min, min));
        }
        return new CoverageResponse(date.ToString("yyyy-MM-dd"), slots, 3);
    }

    public async Task<byte[]> ExportToExcelAsync(ShiftFilterParams f)
    {
        var rows = await GetShiftsAsync(f);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Shift Report");
        var headers = new[] { "ID", "Employee ID", "Full Name", "Engagement", "Primary Role", "Secondary Role", "Team Lead", "Date", "Shift Type", "Start", "End", "WIC Duty", "Task", "Raw Value" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e40af");
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r]; var er = r + 2;
            ws.Cell(er, 1).Value  = row.Id;
            ws.Cell(er, 2).Value  = row.EmployeeId;
            ws.Cell(er, 3).Value  = row.FullName ?? "";
            ws.Cell(er, 4).Value  = row.Engagement ?? "";
            ws.Cell(er, 5).Value  = row.PrimaryRole ?? "";
            ws.Cell(er, 6).Value  = row.SecondaryRole ?? "";
            ws.Cell(er, 7).Value  = row.TeamLeadName ?? "";
            ws.Cell(er, 8).Value  = row.ShiftDate.ToString("yyyy-MM-dd");
            ws.Cell(er, 9).Value  = row.ShiftType;
            ws.Cell(er, 10).Value = row.ShiftStart ?? "";
            ws.Cell(er, 11).Value = row.ShiftEnd ?? "";
            ws.Cell(er, 12).Value = row.IsWicDuty ? "Yes" : "No";
            ws.Cell(er, 13).Value = row.AgentTask ?? "";
            ws.Cell(er, 14).Value = row.RawValue ?? "";
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

public static class ShiftEndpointMapper
{
    public static void MapShiftEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/shifts").WithTags("Shifts");

        grp.MapGet("/", async (string? from, string? to, string? teamLead, string? role, string? engagement, string? shiftType, ShiftService svc) =>
        {
            var f = new ShiftFilterParams(from, to, teamLead, role, engagement, shiftType);
            return Results.Ok(await svc.GetShiftsAsync(f));
        });

        grp.MapGet("/working-today", async (string? date, ShiftService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetWorkingTodayAsync(d));
        });

        grp.MapPatch("/{id:int}", async (int id, ShiftUpdateDto dto, ShiftService svc) =>
        {
            var result = await svc.UpdateShiftAsync(id, dto);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        grp.MapPost("/assign", async (AssignShiftDto dto, ShiftService svc) =>
        {
            var result = await svc.AssignShiftAsync(dto);
            if (result.DuplicateError != null) return Results.Conflict(new { error = result.DuplicateError });
            if (result.Row == null) return Results.BadRequest(new { error = "Invalid date, unknown employee, or date beyond the planning horizon" });
            return Results.Ok(result.Row);
        });

        grp.MapGet("/coverage", async (string? date, ShiftService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetCoverageAsync(d));
        });

        grp.MapPost("/validate", async (ShiftValidateRequest req, ShiftValidationService svc) =>
        {
            var result = await svc.ValidateAsync(req.ShiftId, req.ShiftType, req.ShiftStart, req.ShiftEnd);
            return Results.Ok(result);
        });

        grp.MapPost("/{id:int}/swap", async (int id, SwapShiftDto dto, GSDDashboard.API.Data.GSDContext db) =>
        {
            var original = await db.ShiftEntries.FindAsync(id);
            if (original == null) return Results.NotFound(new { error = "Shift not found" });
            if (string.IsNullOrWhiteSpace(dto.NewEmployeeId))
                return Results.BadRequest(new { error = "NewEmployeeId required" });

            var newEmp = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == dto.NewEmployeeId);
            if (newEmp == null) return Results.NotFound(new { error = $"Employee '{dto.NewEmployeeId}' not found" });

            var date      = original.ShiftDate;
            var shiftType = original.ShiftType ?? "WIC_DUTY";
            var start     = original.ShiftStart;
            var end       = original.ShiftEnd;
            var task      = original.AgentTask;
            var locId     = original.LocationId;
            var oldEmpId  = original.EmployeeId;

            // Soft-delete the original: set to EMPTY
            original.ShiftType    = "EMPTY";
            original.IsWicDuty    = false;
            original.AgentTask    = null;
            original.LocationId   = null;

            // Transfer WicShiftEntries from old to new employee
            var wicEntries = await db.WicShiftEntries
                .Where(w => w.EmployeeId == oldEmpId && w.ShiftDate == date)
                .ToListAsync();
            foreach (var w in wicEntries) w.EmployeeId = dto.NewEmployeeId;

            // Create or update ShiftEntry for the new employee on the same date
            var newShift = await db.ShiftEntries.FirstOrDefaultAsync(s => s.EmployeeId == dto.NewEmployeeId && s.ShiftDate == date);
            if (newShift != null)
            {
                newShift.ShiftType    = shiftType;
                newShift.IsWicDuty    = shiftType == "WIC_DUTY";
                newShift.ShiftStart   = start;
                newShift.ShiftEnd     = end;
                newShift.AgentTask    = task;
                newShift.LocationId   = locId;
                newShift.SourceModule = "SWAP";
            }
            else
            {
                db.ShiftEntries.Add(new ShiftEntry
                {
                    EmployeeId   = dto.NewEmployeeId,
                    ShiftDate    = date,
                    ShiftType    = shiftType,
                    IsWicDuty    = shiftType == "WIC_DUTY",
                    ShiftStart   = start,
                    ShiftEnd     = end,
                    AgentTask    = task,
                    LocationId   = locId,
                    SourceSheet  = "ASSIGN",
                    SourceModule = "SWAP",
                });
            }

            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                swapped    = true,
                date       = date.ToString("yyyy-MM-dd"),
                from       = oldEmpId,
                to         = dto.NewEmployeeId,
                toName     = newEmp.FullName ?? dto.NewEmployeeId,
                shiftType,
            });
        });

        grp.MapGet("/download", async (string? from, string? to, string? teamLead, string? role, ShiftService svc, HttpContext ctx) =>
        {
            var f = new ShiftFilterParams(from, to, teamLead, role, null, null);
            var bytes = await svc.ExportToExcelAsync(f);
            var filename = $"ShiftReport_{DateTime.Today:yyyy-MM-dd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        });
    }
}



