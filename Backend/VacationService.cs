using ClosedXML.Excel;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using ALBalanceModel = GSDDashboard.API.Data.Models.ALBalance;
using Microsoft.EntityFrameworkCore;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Services;

namespace GSDDashboard.API.Modules.Vacations;

public record VacationDto(
    int Id, string? EmployeeId, string? LastName, string? FirstName,
    string FirstDay, string LastDay, decimal? WorkDaysNet,
    string? Comments, string? ApprovedDenied, string? ApproverName,
    string? SourceSheet, bool IsOverhead, string? TeamLeadName
);

public record CreateVacationDto(string EmployeeId, string FirstDay, string LastDay, string? Comments, bool IsHalfDay = false, string? LeaveType = null);

public record DailyLeaveCountDto(
    string Date, int MaxLeave, int TotalOff, int AlCount, int SlCount, int Remaining, bool IsFull
);

// One employee on AL for a single date (full or half), for the "On AL today" card.
public record VacationActiveDto(string EmployeeId, string? FullName, bool IsHalfDay);

// One AL period starting in the upcoming window (full or half), for the "Upcoming 7 days" list.
public record VacationUpcomingDto(string EmployeeId, string? FullName, string FirstDay, string LastDay, bool IsHalfDay);

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
        var empIds = rows.Select(r => r.EmployeeId).Where(x => x != null).Distinct().ToList();
        var empMap = await _db.Employees.Where(e => empIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => e);

        // Compute work-days for rows that never stored a value (new formula: weekdays minus
        // holidays), instead of the old calendar-day fallback.
        var fallbackDays = new Dictionary<int, decimal>();
        foreach (var v in rows.Where(v => v.WorkDaysNet == null && v.EmployeeId != null && empMap.ContainsKey(v.EmployeeId)))
            fallbackDays[v.Id] = await ComputeWorkDaysAsync(empMap[v.EmployeeId!], v.FirstDay, v.LastDay);

        return rows
            .Where(v => v.EmployeeId != null && empMap.ContainsKey(v.EmployeeId))
            .Select(v => Map(v, empMap[v.EmployeeId!], fallbackDays.GetValueOrDefault(v.Id)))
            .ToList();
    }

    public async Task<List<VacationActiveDto>> GetActiveOnDateAsync(DateOnly date)
    {
        // The shared union rule (Shift Plan OR AL Tracker, with the manual-duty override
        // and the rejected/cancelled + inactive/resigned exclusions) — the same source the
        // Overview "Absent today" AL count reads, so the two views can never disagree.
        var result = await AvailabilityResolver.GetAlOnDateAsync(_db, date);
        var names = await NamesForAsync(result.People.Select(p => p.EmployeeId).Distinct().ToList());
        return result.People
            .Select(p => new VacationActiveDto(
                p.EmployeeId, names.TryGetValue(p.EmployeeId, out var n) ? n : null, p.IsHalfDay))
            .ToList();
    }

    public async Task<List<VacationUpcomingDto>> GetUpcomingAsync(int days)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var future = today.AddDays(days);
        var starts = await AvailabilityResolver.GetAlStartingAsync(_db, today, future);
        var names = await NamesForAsync(starts.Select(s => s.EmployeeId).Distinct().ToList());
        return starts
            .Select(s => new VacationUpcomingDto(
                s.EmployeeId,
                names.TryGetValue(s.EmployeeId, out var n) ? n : null,
                s.FirstDay.ToString("yyyy-MM-dd"),
                s.LastDay.ToString("yyyy-MM-dd"),
                s.IsHalfDay))
            .ToList();
    }

    private async Task<Dictionary<string, string?>> NamesForAsync(List<string> empIds)
    {
        if (empIds.Count == 0) return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        return await _db.Employees
            .Where(e => empIds.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e.FullName);
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

    public async Task<VacationDto?> CreateAsync(CreateVacationDto dto, string actor)
    {
        if (!DateOnly.TryParse(dto.FirstDay, out var firstDay) || !DateOnly.TryParse(dto.LastDay, out var lastDay))
            return null;
        if (lastDay < firstDay) return null;
        if (lastDay > DateOnly.FromDateTime(DateTime.Today).AddDays(ScheduleLimits.MaxFutureDays)) return null;

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId && e.IsActive);
        if (emp == null) return null;

        var leaveType = string.IsNullOrWhiteSpace(dto.LeaveType) ? "AL" : dto.LeaveType.Trim().ToUpperInvariant();

        // OL (Other Leave) uses the same date-range picker as AL but is a plain
        // shift-status entry: it does not consume AL balance and is not tracked
        // in the Vacations table. Past start dates are explicitly allowed
        // (e.g. retroactive corrections), unlike AL which is future-oriented.
        if (leaveType == "OL")
        {
            await _shiftSync.SyncVacationAsync(dto.EmployeeId, firstDay, lastDay, 0, "OL", "OtherLeave");
            return new VacationDto(
                0, dto.EmployeeId, emp.LastName, emp.FirstName,
                firstDay.ToString("yyyy-MM-dd"), lastDay.ToString("yyyy-MM-dd"),
                await ComputeWorkDaysAsync(emp, firstDay, lastDay), dto.Comments, null, null,
                emp.SourceSheet, false, emp.TeamLeadName
            );
        }

        var duplicate = await _db.Vacations.FirstOrDefaultAsync(v =>
            v.EmployeeId == dto.EmployeeId && v.FirstDay == firstDay && v.LastDay == lastDay);
        if (duplicate != null) return Map(duplicate, emp);

        // AL and SL must never be merged into one entry. Reject if any day in this
        // AL range is already covered by a SickLeave record for the same employee.
        var hasSickLeaveOverlap = await _db.SickLeaves.AnyAsync(s =>
            s.EmployeeId == dto.EmployeeId && s.FirstDay <= lastDay && s.LastDay >= firstDay);
        if (hasSickLeaveOverlap) return null;

        decimal workDays = dto.IsHalfDay ? 0.5m : await ComputeWorkDaysAsync(emp, firstDay, lastDay);
        string shiftType = dto.IsHalfDay ? "HALF_AL" : "AL";
        // Half-day: first and last day must be the same day
        if (dto.IsHalfDay) lastDay = firstDay;

        var vac = new Vacation
        {
            EmployeeId  = dto.EmployeeId,
            FirstDay    = firstDay,
            LastDay     = lastDay,
            WorkDaysNet = workDays,
            Comments    = dto.Comments,
            SourceSheet = emp.SourceSheet,
            CreatedAt   = DateTime.UtcNow
        };
        _db.Vacations.Add(vac);

        if (workDays > 0)
        {
            var year = BerlinTime.Today.Year;
            var bal = await _db.ALBalances.FirstOrDefaultAsync(b => b.EmployeeId == dto.EmployeeId && b.Year == year);
            if (bal == null)
            {
                bal = new ALBalanceModel { EmployeeId = dto.EmployeeId, EmployeeName = emp.FullName, EligibleDays = 28, Year = year };
                _db.ALBalances.Add(bal);
            }
            bal.PlannedTakenAL = bal.PlannedTakenAL + workDays;
            bal.RemainingAL    = bal.EligibleDays - bal.PlannedTakenAL;
            bal.ManualChangeBy = actor;
            bal.ManualChangeAt = DateTime.UtcNow;
            bal.LastUpdated    = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        await _shiftSync.SyncVacationAsync(dto.EmployeeId, firstDay, lastDay, vac.Id, shiftType);

        return Map(vac, emp);
    }

    // Working days (Mon–Fri) minus public holidays that apply to the employee.
    // National holidays are country-scoped (DE by default; NL for employees with an NL
    // WIC assignment); regional holidays match the employee's Bundesland (R2).
    private async Task<decimal> ComputeWorkDaysAsync(Employee emp, DateOnly from, DateOnly to)
    {
        var country = await DetermineCountryAsync(emp);
        var holidayDates = (await _db.PublicHolidays
                .Where(h => h.HolidayDate >= from && h.HolidayDate <= to)
                .ToListAsync())
            .Where(h => HolidayMatcher.Applies(h, country, emp.Bundesland))
            .Select(h => h.HolidayDate)
            .ToHashSet();

        int count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
            if (holidayDates.Contains(d)) continue;
            count++;
        }
        return count;
    }

    // DE by default; NL when the employee has an active WIC assignment on an NL location.
    private async Task<string> DetermineCountryAsync(Employee emp)
    {
        var nl = await _db.WicAgentAssignments
            .Where(a => a.EmployeeName == emp.FullName && a.IsActive)
            .AnyAsync(a => a.LocationCode.StartsWith("NL"));
        return nl ? "NL" : "DE";
    }


    public async Task<bool> DeleteAsync(int id, string actor)
    {
        var vac = await _db.Vacations.FindAsync(id);
        if (vac == null) return false;

        // Restore AL balance
        if (vac.EmployeeId != null && vac.WorkDaysNet.HasValue && vac.WorkDaysNet.Value > 0)
        {
            var year = BerlinTime.Today.Year;
            var bal = await _db.ALBalances.FirstOrDefaultAsync(b => b.EmployeeId == vac.EmployeeId && b.Year == year);
            if (bal != null)
            {
                bal.PlannedTakenAL = Math.Max(0m, bal.PlannedTakenAL - vac.WorkDaysNet.Value);
                bal.RemainingAL    = bal.EligibleDays - bal.PlannedTakenAL;
                bal.ManualChangeBy = actor;
                bal.ManualChangeAt = DateTime.UtcNow;
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

    private static VacationDto Map(Vacation v, Employee? emp = null, decimal? fallbackDays = null) => new(
        v.Id, v.EmployeeId,
        "",
        emp?.FullName ?? v.FirstName,
        v.FirstDay.ToString("yyyy-MM-dd"), v.LastDay.ToString("yyyy-MM-dd"),
        v.WorkDaysNet ?? fallbackDays,
        v.Comments, v.ApprovedDenied, v.ApproverName,
        v.SourceSheet, v.IsOverhead, emp?.TeamLeadName
    );
}

public static class VacationEndpointMapper
{
    // Prefer the logged-in user's KID; fall back to a fixed tag while
    // Auth:EnforceAuthorization is still in its rollout phase (same contract as
    // WicShiftService.ResolveManualActor).
    private static string ResolveManualActor(HttpContext ctx) =>
        ctx.User.FindFirst(AuthClaims.Kid)?.Value is { Length: > 0 and <= 100 } kid ? kid : "UI-MANUAL";

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

        grp.MapPost("/", async (CreateVacationDto dto, VacationService svc, HttpContext ctx) =>
        {
            var vac = await svc.CreateAsync(dto, ResolveManualActor(ctx));
            return vac == null ? Results.BadRequest("Invalid date range or unknown employee") : Results.Ok(vac);
        });

        grp.MapDelete("/{id:int}", async (int id, VacationService svc, HttpContext ctx) =>
        {
            var ok = await svc.DeleteAsync(id, ResolveManualActor(ctx));
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



