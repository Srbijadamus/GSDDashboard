using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.WicShifts;

public record WicShiftDto(
    int Id, string EmployeeId, string? FullName, string? TeamLeadName,
    string ShiftDate, string? DayOfWeek, string? SupportLocation,
    string? WicOpeningHours, string? WorkingShift,
    bool IsOnSite, bool IsGSDDay, bool IsOffDay
);

public record WicCoverageDto(
    string LocationCode, string DisplayName, string City, string Country,
    string? OpeningSchedule, bool IsCovered, int AgentCount,
    List<WicAgentInfo> Agents
);

public record WicAgentInfo(string EmployeeId, string? Name, string? TeamLead, string? WorkingShift, string? WicOpeningHours);

public class WicShiftService
{
    private readonly GSDContext _db;
    public WicShiftService(GSDContext db) => _db = db;

    public async Task<List<WicShiftDto>> GetWicShiftsAsync(
        string? from, string? to, string? location, string? employeeId, string? teamLead)
    {
        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : fromDate;

        var q = _db.WicShiftEntries
            .Where(w => w.ShiftDate >= fromDate && w.ShiftDate <= toDate)
            .Join(_db.Employees, w => w.EmployeeId, e => e.EmployeeId,
                  (w, e) => new { Wic = w, Emp = e });

        if (!string.IsNullOrWhiteSpace(location))
            q = q.Where(x => x.Wic.SupportLocation == location);
        if (!string.IsNullOrWhiteSpace(employeeId))
            q = q.Where(x => x.Wic.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(teamLead))
            q = q.Where(x => x.Emp.TeamLeadName == teamLead);

        var rows = await q.OrderBy(x => x.Wic.ShiftDate).ThenBy(x => x.Emp.FullName).ToListAsync();
        return rows.Select(x => new WicShiftDto(
            x.Wic.Id, x.Wic.EmployeeId,
            x.Emp.FullName ?? x.Emp.EmployeeId,
            x.Emp.TeamLeadName,
            x.Wic.ShiftDate.ToString("yyyy-MM-dd"),
            x.Wic.DayOfWeek, x.Wic.SupportLocation,
            x.Wic.WicOpeningHours, x.Wic.WorkingShift,
            x.Wic.IsOnSite, x.Wic.IsGSDDay, x.Wic.IsOffDay
        )).ToList();
    }

    public async Task<List<WicLocation>> GetLocationsAsync() =>
        await _db.WicLocations.Where(l => l.IsActive).OrderBy(l => l.Country).ThenBy(l => l.City).ToListAsync();

    public async Task<List<WicCoverageDto>> GetCoverageAsync(DateOnly date)
    {
        var locations = await _db.WicLocations.Where(l => l.IsActive).ToListAsync();
        var entries = await _db.WicShiftEntries
            .Where(w => w.ShiftDate == date && w.IsOnSite)
            .Join(_db.Employees, w => w.EmployeeId, e => e.EmployeeId,
                  (w, e) => new { w, e })
            .ToListAsync();

        var byLocation = entries
            .GroupBy(x => x.w.SupportLocation ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());

        return locations.Select(loc =>
        {
            var agents = byLocation.TryGetValue(loc.DisplayName, out var list) ? list : [];
            var agentInfos = agents.Select(x => new WicAgentInfo(
                x.e.EmployeeId,
                x.e.FullName ?? x.e.EmployeeId,
                x.e.TeamLeadName?.Trim(),
                x.w.WorkingShift,
                x.w.WicOpeningHours
            )).ToList();

            return new WicCoverageDto(
                loc.LocationCode, loc.DisplayName,
                loc.City ?? "", loc.Country ?? "DE",
                loc.OpeningSchedule,
                agentInfos.Count > 0,
                agentInfos.Count,
                agentInfos
            );
        }).ToList();
    }

    public async Task<byte[]> ExportToExcelAsync(string? from, string? to)
    {
        var rows = await GetWicShiftsAsync(from, to, null, null, null);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("WIC Shifts");
        var headers = new[] { "Emp ID", "Name", "Team Lead", "Date", "Day", "Location", "Opening Hours", "Working Shift", "On Site", "GSD Day" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0e7490");
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r]; var er = r + 2;
            ws.Cell(er, 1).Value = row.EmployeeId;
            ws.Cell(er, 2).Value = row.FullName ?? "";
            ws.Cell(er, 3).Value = row.TeamLeadName ?? "";
            ws.Cell(er, 4).Value = row.ShiftDate;
            ws.Cell(er, 5).Value = row.DayOfWeek ?? "";
            ws.Cell(er, 6).Value = row.SupportLocation ?? "";
            ws.Cell(er, 7).Value = row.WicOpeningHours ?? "";
            ws.Cell(er, 8).Value = row.WorkingShift ?? "";
            ws.Cell(er, 9).Value = row.IsOnSite ? "Yes" : "No";
            ws.Cell(er, 10).Value = row.IsGSDDay ? "Yes" : "No";
            if (row.IsOnSite)
                ws.Row(er).Style.Fill.BackgroundColor = XLColor.FromHtml("#cffafe");
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

public static class WicEndpointMapper
{
    public static void MapWicEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/wic").WithTags("WIC");

        grp.MapGet("/", async (string? from, string? to, string? location, string? employeeId, string? teamLead, WicShiftService svc) =>
            Results.Ok(await svc.GetWicShiftsAsync(from, to, location, employeeId, teamLead)));

        grp.MapGet("/locations", async (WicShiftService svc) =>
            Results.Ok(await svc.GetLocationsAsync()));

        grp.MapGet("/coverage", async (string? date, WicShiftService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetCoverageAsync(d));
        });

        grp.MapGet("/agent/{employeeId}", async (string employeeId, string? from, string? to, WicShiftService svc) =>
            Results.Ok(await svc.GetWicShiftsAsync(from, to, null, employeeId, null)));

        grp.MapGet("/download", async (string? from, string? to, WicShiftService svc, HttpContext ctx) =>
        {
            var bytes = await svc.ExportToExcelAsync(from, to);
            var filename = $"WicReport_{DateTime.Today:yyyy-MM-dd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        });
    }
}
