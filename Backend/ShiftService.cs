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

public record ShiftUpdateDto(
    string? ShiftType,
    string? ShiftStart,
    string? ShiftEnd,
    string? AgentTask,
    string? LocationId,
    string? AssignmentStatus
);

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
