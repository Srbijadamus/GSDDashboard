using ClosedXML.Excel;
using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Attendance;

public record AttendanceDto(
    int Id, string LocationName, string? Country,
    string AttendanceDate, string? RawValue,
    string? AttendanceType, string? AssignedEmployeeId
);

public class AttendanceService
{
    private readonly GSDContext _db;
    public AttendanceService(GSDContext db) => _db = db;

    public async Task<List<AttendanceDto>> GetAttendanceAsync(
        string? from, string? to, string? country, string? location)
    {
        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : fromDate;

        var q = _db.DailyAttendances
            .Where(a => a.AttendanceDate >= fromDate && a.AttendanceDate <= toDate);

        if (!string.IsNullOrWhiteSpace(country))
            q = q.Where(a => a.Country == country);
        if (!string.IsNullOrWhiteSpace(location))
            q = q.Where(a => a.LocationName.Contains(location));

        var rows = await q.OrderBy(a => a.AttendanceDate).ThenBy(a => a.LocationName).ToListAsync();
        return rows.Select(a => new AttendanceDto(
            a.Id, a.LocationName, a.Country,
            a.AttendanceDate.ToString("yyyy-MM-dd"),
            a.RawValue, a.AttendanceType, a.AssignedEmployeeId
        )).ToList();
    }

    public async Task<List<string>> GetLocationsAsync(string? country)
    {
        var q = _db.DailyAttendances.AsQueryable();
        if (!string.IsNullOrWhiteSpace(country))
            q = q.Where(a => a.Country == country);
        return await q.Select(a => a.LocationName).Distinct().OrderBy(x => x).ToListAsync();
    }

    public async Task<byte[]> ExportToExcelAsync(string? from, string? to)
    {
        var rows = await GetAttendanceAsync(from, to, null, null);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Attendance");
        var headers = new[] { "Location", "Country", "Date", "Employee ID", "Status", "Raw Value" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r]; var er = r + 2;
            ws.Cell(er, 1).Value = row.LocationName;
            ws.Cell(er, 2).Value = row.Country ?? "";
            ws.Cell(er, 3).Value = row.AttendanceDate;
            ws.Cell(er, 4).Value = row.AssignedEmployeeId ?? "";
            ws.Cell(er, 5).Value = row.AttendanceType ?? "";
            ws.Cell(er, 6).Value = row.RawValue ?? "";
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

public static class AttendanceEndpointMapper
{
    public static void MapAttendanceEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/attendance").WithTags("Attendance");

        grp.MapGet("/", async (string? from, string? to, string? country, string? location, AttendanceService svc) =>
            Results.Ok(await svc.GetAttendanceAsync(from, to, country, location)));

        grp.MapGet("/locations", async (string? country, AttendanceService svc) =>
            Results.Ok(await svc.GetLocationsAsync(country)));

        grp.MapGet("/download", async (string? from, string? to, AttendanceService svc, HttpContext ctx) =>
        {
            var bytes = await svc.ExportToExcelAsync(from, to);
            var filename = $"Attendance_{DateTime.Today:yyyy-MM-dd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        });
    }
}
