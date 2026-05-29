using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

using SickLeaveModel = GSDDashboard.API.Data.Models.SickLeave;
namespace GSDDashboard.API.Modules.SickLeave;

public record SickLeaveDto(
    int Id,
    string? EmployeeId,
    string? FirstName,
    string? LastName,
    string? FullName,
    string? TeamLeadName,
    string FirstDay,
    string LastDay,
    int? DurationDays,
    string? LeaveType,
    string? ChildName,
    string? Comments,
    string? SourceSheet
);

public record SickLeaveStatsDto(
    int TotalActive,
    double AverageDuration,
    int SelfCount,
    int ChildCount,
    List<TLBreakdownDto> ByTeamLead
);

public record TLBreakdownDto(string TeamLead, int Count);

public record CreateSickLeaveRequest(
    string? EmployeeId,
    string? FirstName,
    string? LastName,
    string StartDate,
    string EndDate,
    string? Type,
    string? ChildName,
    string? Notes
);

public record PatchSickLeaveRequest(
    string? StartDate,
    string? EndDate,
    string? Type,
    string? Notes
);

public class SickLeaveService
{
    private readonly GSDContext _db;
    public SickLeaveService(GSDContext db) => _db = db;

    public async Task<List<SickLeaveDto>> GetSickLeavesAsync(
        string? from, string? to, string? teamLead, string? type, bool? activeOnly)
    {
        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.MinValue;
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : DateOnly.MaxValue;
        var today    = DateOnly.FromDateTime(DateTime.Today);

        IQueryable<SickLeaveModel> query = _db.SickLeaves;

        if (activeOnly == true)
            query = query.Where(s => s.FirstDay <= today && s.LastDay >= today);
        else
            query = query.Where(s => s.FirstDay <= toDate && s.LastDay >= fromDate);

        if (!string.IsNullOrWhiteSpace(teamLead))
            query = query.Where(s => s.TeamLeadName == teamLead);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(s => s.LeaveType == type);
        var rows = await query.OrderBy(s => s.TeamLeadName).ThenBy(s => s.LastName).ToListAsync();
        var empIds = rows.Select(r => r.EmployeeId).Where(x => x != null).Distinct().ToList();
        var empMap = await _db.Employees.Where(e => empIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => e);
        return rows.Select(s => { var emp = s.EmployeeId != null && empMap.TryGetValue(s.EmployeeId, out var ev) ? ev : null; return new SickLeaveDto(s.Id, s.EmployeeId, emp?.FirstName ?? s.FirstName, emp?.LastName ?? s.LastName, emp?.FullName ?? s.FullName, emp?.TeamLeadName ?? s.TeamLeadName, s.FirstDay.ToString("yyyy-MM-dd"), s.LastDay.ToString("yyyy-MM-dd"), s.DurationDays, s.LeaveType, s.ChildName, s.Comments, s.SourceSheet); }).ToList();
    }

    public async Task<List<SickLeaveDto>> GetActiveOnDateAsync(DateOnly date)
    {
        var rows = await _db.SickLeaves
            .Where(s => s.FirstDay <= date && s.LastDay >= date)
            .OrderBy(s => s.TeamLeadName)
            .ThenBy(s => s.LastName)
            .ToListAsync();
        var empIds = rows.Select(r => r.EmployeeId).Where(x => x != null).Distinct().ToList();
        var empMap = await _db.Employees.Where(e => empIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => e);
        return rows.Select(s => { var emp = s.EmployeeId != null && empMap.TryGetValue(s.EmployeeId, out var ev) ? ev : null; return new SickLeaveDto(s.Id, s.EmployeeId, emp?.FirstName ?? s.FirstName, emp?.LastName ?? s.LastName, emp?.FullName ?? s.FullName, emp?.TeamLeadName ?? s.TeamLeadName, s.FirstDay.ToString("yyyy-MM-dd"), s.LastDay.ToString("yyyy-MM-dd"), s.DurationDays, s.LeaveType, s.ChildName, s.Comments, s.SourceSheet); }).ToList();
    }

    public async Task<SickLeaveStatsDto> GetStatsAsync(string? from, string? to)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var active = await _db.SickLeaves
            .Where(s => s.FirstDay <= today && s.LastDay >= today)
            .ToListAsync();
        var avgDuration = active.Any()
            ? active.Where(s => s.DurationDays.HasValue).Average(s => (double)s.DurationDays!.Value)
            : 0;

        var byTL = active
            .GroupBy(s => s.TeamLeadName?.Trim() ?? "Unknown")
            .Select(g => new TLBreakdownDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        return new SickLeaveStatsDto(
            active.Count,
            Math.Round(avgDuration, 1),
            active.Count(s => s.LeaveType == "Self"),
            active.Count(s => s.LeaveType == "Child"),
            byTL
        );
    }

    public async Task<SickLeaveDto> CreateAsync(CreateSickLeaveRequest req)
    {
        var emp = req.EmployeeId != null ? await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId) : null;
        var start = DateOnly.Parse(req.StartDate);
        var end   = DateOnly.Parse(req.EndDate);
        var entry = new SickLeaveModel
        {
            EmployeeId   = req.EmployeeId,
            FirstName    = emp?.FirstName    ?? req.FirstName,
            LastName     = emp?.LastName     ?? req.LastName,
            TeamLeadName = emp?.TeamLeadName,
            FirstDay     = start,
            LastDay      = end,
            DurationDays = (end.DayNumber - start.DayNumber) + 1,
            LeaveType    = req.Type ?? "Self",
            ChildName    = req.ChildName,
            Comments     = req.Notes,
            SourceSheet  = "UI",
        };
        _db.SickLeaves.Add(entry);
        await _db.SaveChangesAsync();
        return new SickLeaveDto(entry.Id, entry.EmployeeId, entry.FirstName, entry.LastName,
            ((entry.FirstName ?? "") + " " + (entry.LastName ?? "")).Trim(),
            entry.TeamLeadName, entry.FirstDay.ToString("yyyy-MM-dd"), entry.LastDay.ToString("yyyy-MM-dd"),
            entry.DurationDays, entry.LeaveType, entry.ChildName, entry.Comments, entry.SourceSheet);
    }

    public async Task<SickLeaveDto?> PatchAsync(int id, PatchSickLeaveRequest req)
    {
        var entry = await _db.SickLeaves.FindAsync(id);
        if (entry == null) return null;
        if (req.EndDate   != null) { entry.LastDay  = DateOnly.Parse(req.EndDate);   entry.DurationDays = (entry.LastDay.DayNumber - entry.FirstDay.DayNumber) + 1; }
        if (req.StartDate != null) { entry.FirstDay = DateOnly.Parse(req.StartDate); entry.DurationDays = (entry.LastDay.DayNumber - entry.FirstDay.DayNumber) + 1; }
        if (req.Type      != null) entry.LeaveType = req.Type;
        if (req.Notes     != null) entry.Comments  = req.Notes;
        await _db.SaveChangesAsync();
        return new SickLeaveDto(entry.Id, entry.EmployeeId, entry.FirstName, entry.LastName,
            ((entry.FirstName ?? "") + " " + (entry.LastName ?? "")).Trim(),
            entry.TeamLeadName, entry.FirstDay.ToString("yyyy-MM-dd"), entry.LastDay.ToString("yyyy-MM-dd"),
            entry.DurationDays, entry.LeaveType, entry.ChildName, entry.Comments, entry.SourceSheet);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entry = await _db.SickLeaves.FindAsync(id);
        if (entry == null) return false;
        _db.SickLeaves.Remove(entry);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<byte[]> ExportToExcelAsync(string? from, string? to)
    {
        var rows = await GetSickLeavesAsync(from, to, null, null, null);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sick Leave");
        var headers = new[] { "Employee ID", "First Name", "Last Name", "Team Lead", "First Day", "Last Day", "Duration (Days)", "Type", "Child Name", "Comments", "Source Sheet" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#b91c1c");
            ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r]; var er = r + 2;
            ws.Cell(er, 1).Value  = row.EmployeeId ?? "";
            ws.Cell(er, 2).Value  = row.FirstName ?? "";
            ws.Cell(er, 3).Value  = row.LastName ?? "";
            ws.Cell(er, 4).Value  = row.TeamLeadName ?? "";
            ws.Cell(er, 5).Value  = row.FirstDay;
            ws.Cell(er, 6).Value  = row.LastDay;
            ws.Cell(er, 7).Value  = row.DurationDays ?? 0;
            ws.Cell(er, 8).Value  = row.LeaveType ?? "";
            ws.Cell(er, 9).Value  = row.ChildName ?? "";
            ws.Cell(er, 10).Value = row.Comments ?? "";
            ws.Cell(er, 11).Value = row.SourceSheet ?? "";
            if (row.DurationDays > 30)
                ws.Row(er).Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");
            else if (row.DurationDays >= 14)
                ws.Row(er).Style.Fill.BackgroundColor = XLColor.FromHtml("#ffedd5");
            else if (row.DurationDays >= 7)
                ws.Row(er).Style.Fill.BackgroundColor = XLColor.FromHtml("#fef9c3");
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

public static class SickLeaveEndpointMapper
{
    public static void MapSickLeaveEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/sickleave").WithTags("SickLeave");

        grp.MapGet("/", async (string? from, string? to, string? teamLead, string? type, bool? activeOnly, SickLeaveService svc) =>
            Results.Ok(await svc.GetSickLeavesAsync(from, to, teamLead, type, activeOnly)));

        grp.MapGet("/active", async (string? date, SickLeaveService svc) =>
        {
            var d = date != null && DateOnly.TryParse(date, out var pd) ? pd : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetActiveOnDateAsync(d));
        });

        grp.MapGet("/stats", async (string? from, string? to, SickLeaveService svc) =>
            Results.Ok(await svc.GetStatsAsync(from, to)));

        grp.MapPost("/", async (CreateSickLeaveRequest req, SickLeaveService svc) =>
        {
            var result = await svc.CreateAsync(req);
            return Results.Created($"/api/sickleave/{result.Id}", result);
        });

        grp.MapPatch("/{id:int}", async (int id, PatchSickLeaveRequest req, SickLeaveService svc) =>
        {
            var result = await svc.PatchAsync(id, req);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        grp.MapDelete("/{id:int}", async (int id, SickLeaveService svc) =>
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        grp.MapGet("/download", async (string? from, string? to, SickLeaveService svc, HttpContext ctx) =>
        {
            var bytes = await svc.ExportToExcelAsync(from, to);
            var filename = $"SickLeave_{DateTime.Today:yyyy-MM-dd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        });
    }
}
