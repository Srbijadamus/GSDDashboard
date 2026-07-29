using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

using SickLeaveModel = GSDDashboard.API.Data.Models.SickLeave;
using GSDDashboard.API.Services;
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
    private readonly ShiftSyncService _shiftSync;
    public SickLeaveService(GSDContext db, ShiftSyncService shiftSync) { _db = db; _shiftSync = shiftSync; }

    public async Task<List<SickLeaveDto>> GetSickLeavesAsync(
        string? from, string? to, string? teamLead, string? type, bool? activeOnly)
    {
        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.MinValue;
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : DateOnly.MaxValue;
        var today    = DateOnly.FromDateTime(DateTime.Today);

        IQueryable<SickLeaveModel> query = _db.SickLeaves;

        if (activeOnly == true)
            query = query.Where(s => s.FirstDay <= today && s.LastDay >= today
                                  && (s.LeaveType == "SL" || s.LeaveType == "Self"));
        else
            query = query.Where(s => s.FirstDay <= toDate && s.LastDay >= fromDate);

        if (!string.IsNullOrWhiteSpace(teamLead))
            query = query.Where(s => s.TeamLeadName == teamLead);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(s => s.LeaveType == type);
        var rows = await query.OrderBy(s => s.TeamLeadName).ThenBy(s => s.LastName).ToListAsync();
        var empIds = rows.Select(r => r.EmployeeId).Where(x => x != null).Distinct().ToList();
        var empMap = await _db.Employees.Where(e => empIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => e);
        return rows
            .Where(s => s.EmployeeId != null && empMap.ContainsKey(s.EmployeeId))
            .Select(s => MapSl(s, empMap[s.EmployeeId!]))
            .ToList();
    }

    public async Task<List<SickLeaveDto>> GetActiveOnDateAsync(DateOnly date)
    {
        var rows = await _db.SickLeaves
            .Where(s => s.FirstDay <= date && s.LastDay >= date
                     && (s.LeaveType == "SL" || s.LeaveType == "Self"))
            .OrderBy(s => s.TeamLeadName)
            .ThenBy(s => s.LastName)
            .ToListAsync();
        var empIds = rows.Select(r => r.EmployeeId).Where(x => x != null).Distinct().ToList();
        var empMap = await _db.Employees.Where(e => empIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => e);
        return rows
            .Where(s => s.EmployeeId != null && empMap.ContainsKey(s.EmployeeId))
            .Select(s => MapSl(s, empMap[s.EmployeeId!]))
            .ToList();
    }

    public async Task<SickLeaveStatsDto> GetStatsAsync(string? from, string? to)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var active = await _db.SickLeaves
            .Where(s => s.FirstDay <= today && s.LastDay >= today
                     && (s.LeaveType == "SL" || s.LeaveType == "Self"))
            .ToListAsync();
        var activeEmpIds = active.Select(s => s.EmployeeId).Where(x => x != null).Distinct().ToList();
        var activeEmpMap = await _db.Employees.Where(e => activeEmpIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => e);
        active = active.Where(s => s.EmployeeId != null && activeEmpMap.ContainsKey(s.EmployeeId)).ToList();
        var avgDuration = active.Any()
            ? active.Where(s => s.DurationDays.HasValue).Average(s => (double)s.DurationDays!.Value)
            : 0;

        var byTL = active
            .GroupBy(s => {
                activeEmpMap.TryGetValue(s.EmployeeId ?? "", out var emp);
                return (emp?.TeamLeadName ?? s.TeamLeadName)?.Trim() ?? "Unknown";
            })
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

    private static SickLeaveDto MapSl(SickLeaveModel s, Employee? emp = null)
    {
        var fullName = !string.IsNullOrWhiteSpace(emp?.FullName)  ? emp!.FullName
                     : !string.IsNullOrWhiteSpace(emp?.FirstName) ? $"{emp.FirstName} {emp.LastName}".Trim()
                     : !string.IsNullOrWhiteSpace(s.FirstName)    ? $"{s.FirstName} {s.LastName}".Trim()
                     : null;
        var teamLead = emp?.TeamLeadName ?? s.TeamLeadName;
        return new SickLeaveDto(s.Id, s.EmployeeId, null, null, fullName, teamLead,
            s.FirstDay.ToString("yyyy-MM-dd"), s.LastDay.ToString("yyyy-MM-dd"),
            s.DurationDays, s.LeaveType, s.ChildName, s.Comments, s.SourceSheet);
    }

    public async Task<SickLeaveDto?> CreateAsync(CreateSickLeaveRequest req)
    {
        if (!DateOnly.TryParse(req.StartDate, out var start)) return null;
        if (!DateOnly.TryParse(req.EndDate,   out var end))   return null;
        var emp = req.EmployeeId != null ? await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == req.EmployeeId) : null;

        if (req.EmployeeId != null)
        {
            var duplicate = await _db.SickLeaves.FirstOrDefaultAsync(s =>
                s.EmployeeId == req.EmployeeId && s.FirstDay == start && s.LastDay == end);
            if (duplicate != null) return new SickLeaveDto(duplicate.Id, duplicate.EmployeeId, duplicate.FirstName, duplicate.LastName,
                ((duplicate.FirstName ?? "") + " " + (duplicate.LastName ?? "")).Trim(),
                duplicate.TeamLeadName, duplicate.FirstDay.ToString("yyyy-MM-dd"), duplicate.LastDay.ToString("yyyy-MM-dd"),
                duplicate.DurationDays, duplicate.LeaveType, duplicate.ChildName, duplicate.Comments, duplicate.SourceSheet);

            // AL and SL must never be merged into one entry. Reject if any day in the
            // near-term start of this SL is covered by a Vacation (AL) record.
            // Use a 14-day window so open-ended SL (end=2099-12-31) doesn't block
            // distant future vacations that are separate records, not a merge.
            var guardEnd = end > start.AddDays(14) ? start.AddDays(14) : end;
            var hasVacationOverlap = await _db.Vacations.AnyAsync(v =>
                v.EmployeeId == req.EmployeeId && v.FirstDay <= guardEnd && v.LastDay >= start);
            if (hasVacationOverlap) return null;
        }

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
        await _shiftSync.SyncSickLeaveAsync(entry.EmployeeId ?? "", entry.FirstDay, entry.LastDay, entry.Id);
        return new SickLeaveDto(entry.Id, entry.EmployeeId, entry.FirstName, entry.LastName,
            ((entry.FirstName ?? "") + " " + (entry.LastName ?? "")).Trim(),
            entry.TeamLeadName, entry.FirstDay.ToString("yyyy-MM-dd"), entry.LastDay.ToString("yyyy-MM-dd"),
            entry.DurationDays, entry.LeaveType, entry.ChildName, entry.Comments, entry.SourceSheet);
    }

    public async Task<SickLeaveDto?> PatchAsync(int id, PatchSickLeaveRequest req)
    {
        var entry = await _db.SickLeaves.FindAsync(id);
        if (entry == null) return null;
        var oldFrom = entry.FirstDay;
        var oldTo   = entry.LastDay;
        if (req.EndDate   != null && DateOnly.TryParse(req.EndDate,   out var newEnd))   { entry.LastDay  = newEnd;   entry.DurationDays = (entry.LastDay.DayNumber  - entry.FirstDay.DayNumber) + 1; }
        if (req.StartDate != null && DateOnly.TryParse(req.StartDate, out var newStart)) { entry.FirstDay = newStart; entry.DurationDays = (entry.LastDay.DayNumber  - entry.FirstDay.DayNumber) + 1; }
        if (req.Type      != null) entry.LeaveType = req.Type;
        if (req.Notes     != null) entry.Comments  = req.Notes;
        await _db.SaveChangesAsync();
        await _shiftSync.RevertSickLeaveAsync(entry.EmployeeId ?? "", oldFrom, oldTo, entry.Id);
        await _shiftSync.SyncSickLeaveAsync(entry.EmployeeId ?? "", entry.FirstDay, entry.LastDay, entry.Id);
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
        if (entry.EmployeeId != null)
            await _shiftSync.RevertSickLeaveAsync(entry.EmployeeId, entry.FirstDay, entry.LastDay, entry.Id);
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
            return result == null
                ? Results.BadRequest(new { error = "Invalid date format. Expected yyyy-MM-dd." })
                : Results.Created($"/api/sickleave/{result.Id}", result);
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

