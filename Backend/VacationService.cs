using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;
using GSDDashboard.API.Services;

namespace GSDDashboard.API.Modules.Vacations;

public record VacationDto(
    int Id, string? EmployeeId, string? LastName, string? FirstName,
    string FirstDay, string LastDay, int? WorkDaysNet,
    string? Comments, string? ApprovedDenied, string? ApproverName,
    string? SourceSheet, bool IsOverhead
);

public record DailyLeaveCountDto(
    string Date, int MaxLeave, int TotalOff, int AlCount, int SlCount, int Remaining, bool IsFull
);

public class VacationService
{
    private readonly GSDContext _db;
    private readonly ShiftSyncService _shiftSync;
    public VacationService(GSDContext db, ShiftSyncService shiftSync) { _db = db; _shiftSync = shiftSync; }

    public async Task<List<VacationDto>> GetVacationsAsync(
        string? from, string? to, int? year, string? sheet, string? employeeId)
    {
        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.MinValue;
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : DateOnly.MaxValue;

        var q = _db.Vacations.AsQueryable();

        if (year.HasValue)
            q = q.Where(v => v.FirstDay.Year == year.Value || v.LastDay.Year == year.Value);
        else
            q = q.Where(v => v.FirstDay <= toDate && v.LastDay >= fromDate);

        if (!string.IsNullOrWhiteSpace(sheet))
            q = q.Where(v => v.SourceSheet == sheet);
        if (!string.IsNullOrWhiteSpace(employeeId))
            q = q.Where(v => v.EmployeeId == employeeId);

        var rows = await q.OrderBy(v => v.FirstDay).ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<List<VacationDto>> GetActiveOnDateAsync(DateOnly date)
    {
        var rows = await _db.Vacations
            .Where(v => v.FirstDay <= date && v.LastDay >= date)
            .OrderBy(v => v.LastName)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<List<VacationDto>> GetUpcomingAsync(int days)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var future = today.AddDays(days);
        var rows = await _db.Vacations
            .Where(v => v.FirstDay >= today && v.FirstDay <= future)
            .OrderBy(v => v.FirstDay)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }


    public async Task<List<DailyLeaveCountDto>> GetLeaveAvailabilityAsync(string from, string to, int maxLeave)
    {
        var fromDate = DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
        var toDate   = DateOnly.TryParse(to,   out var td) ? td : fromDate.AddDays(13);

        var vacations  = await _db.Vacations.Where(v => v.FirstDay <= toDate && v.LastDay >= fromDate).ToListAsync();
        var sickLeaves = await _db.SickLeaves.Where(s => s.FirstDay <= toDate && s.LastDay >= fromDate).ToListAsync();

        var result = new List<DailyLeaveCountDto>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
            int alCount = vacations.Count(v => v.FirstDay <= d && v.LastDay >= d);
            int slCount = sickLeaves.Count(s => s.FirstDay <= d && s.LastDay >= d);
            int total = alCount + slCount;
            result.Add(new DailyLeaveCountDto(d.ToString("yyyy-MM-dd"), maxLeave, total, alCount, slCount, Math.Max(0, maxLeave - total), total >= maxLeave));
        }
        return result;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var vac = await _db.Vacations.FindAsync(id);
        if (vac == null) return false;

        // Restore AL balance
        if (vac.EmployeeId != null && vac.WorkDaysNet.HasValue && vac.WorkDaysNet.Value > 0)
        {
            var bal = await _db.ALBalances.FirstOrDefaultAsync(b => b.EmployeeId == vac.EmployeeId);
            if (bal != null)
            {
                bal.PlannedTakenAL = Math.Max(0, bal.PlannedTakenAL - vac.WorkDaysNet.Value);
                bal.RemainingAL    = bal.EligibleDays - bal.PlannedTakenAL;
                bal.LastUpdated    = DateTime.UtcNow;
            }
        }

        _db.Vacations.Remove(vac);
        await _db.SaveChangesAsync();
        if (vac.EmployeeId != null)
            await _shiftSync.RevertVacationAsync(vac.EmployeeId, vac.FirstDay, vac.LastDay, vac.Id);
        return true;
    }

    public async Task<byte[]> ExportToExcelAsync(string? from, string? to)
    {
        var rows = await GetVacationsAsync(from, to, null, null, null);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Vacations");
        var headers = new[] { "EID", "Last Name", "First Name", "First Day", "Last Day", "Work Days Net", "Comments", "Status", "Approver", "Sheet" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#166534");
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r]; var er = r + 2;
            ws.Cell(er, 1).Value = row.EmployeeId ?? "";
            ws.Cell(er, 2).Value = row.LastName ?? "";
            ws.Cell(er, 3).Value = row.FirstName ?? "";
            ws.Cell(er, 4).Value = row.FirstDay;
            ws.Cell(er, 5).Value = row.LastDay;
            ws.Cell(er, 6).Value = row.WorkDaysNet ?? 0;
            ws.Cell(er, 7).Value = row.Comments ?? "";
            ws.Cell(er, 8).Value = row.ApprovedDenied ?? "";
            ws.Cell(er, 9).Value = row.ApproverName ?? "";
            ws.Cell(er, 10).Value = row.SourceSheet ?? "";
            if (row.IsOverhead)
                ws.Row(er).Style.Fill.BackgroundColor = XLColor.FromHtml("#ede9fe");
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static VacationDto Map(Vacation v) => new(
        v.Id, v.EmployeeId, v.LastName, v.FirstName,
        v.FirstDay.ToString("yyyy-MM-dd"), v.LastDay.ToString("yyyy-MM-dd"),
        v.WorkDaysNet, v.Comments, v.ApprovedDenied, v.ApproverName,
        v.SourceSheet, v.IsOverhead
    );
}

public static class VacationEndpointMapper
{
    public static void MapVacationEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/vacations").WithTags("Vacations");

        grp.MapGet("/", async (string? from, string? to, int? year, string? sheet, string? employeeId, VacationService svc) =>
            Results.Ok(await svc.GetVacationsAsync(from, to, year, sheet, employeeId)));

        grp.MapGet("/active", async (string? date, VacationService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetActiveOnDateAsync(d));
        });

        grp.MapGet("/upcoming", async (int? days, VacationService svc) =>
            Results.Ok(await svc.GetUpcomingAsync(days ?? 7)));


        grp.MapGet("/availability", async (string? from, string? to, int? maxLeave, VacationService svc) =>
        {
            var f = from ?? DateTime.Today.ToString("yyyy-MM-dd");
            var t = to   ?? DateTime.Today.AddDays(13).ToString("yyyy-MM-dd");
            return Results.Ok(await svc.GetLeaveAvailabilityAsync(f, t, maxLeave ?? 8));
        });

        grp.MapDelete("/{id:int}", async (int id, VacationService svc) =>
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.Ok(new { deleted = true }) : Results.NotFound();
        });

        grp.MapGet("/download", async (string? from, string? to, VacationService svc, HttpContext ctx) =>
        {
            var bytes = await svc.ExportToExcelAsync(from, to);
            var filename = $"Vacations_{DateTime.Today:yyyy-MM-dd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        });
    }
}



