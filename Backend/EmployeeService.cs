using GSDDashboard.API.Data;
using ALBalanceModel = GSDDashboard.API.Data.Models.ALBalance;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Employees;

public record EmployeeDto(
    int Id, string EmployeeId, string? FirstName, string? LastName, string? FullName,
    string? Engagement, string? PrimaryRole, string? SecondaryRole,
    string? TeamLeadName, string? Category, bool IsActive, bool IsTrainee,
    string? PlannedRole, string? SourceSheet, string? Birthday, string? Bundesland,
    string? ShiftPattern
);

public record ShiftTimelineItem(string Date, string ShiftType, string? ShiftStart, string? ShiftEnd, bool IsWicDuty);
public record SickLeaveItem(string FirstDay, string LastDay, int? DurationDays, string? LeaveType);
public record VacationItem(string FirstDay, string LastDay, int? WorkDaysNet, string? Comments);

public record EmployeeTimelineDto(
    string EmployeeId, string? FullName, string? TeamLeadName,
    List<ShiftTimelineItem> Shifts,
    List<SickLeaveItem> SickLeaves,
    List<VacationItem> Vacations
);

public class EmployeeService
{
    private readonly GSDContext _db;
    public EmployeeService(GSDContext db) => _db = db;

    public async Task<List<EmployeeDto>> GetEmployeesAsync(
        string? role, string? engagement, string? teamLead, string? category, bool? active)
    {
        var q = _db.Employees.AsQueryable();
        if (!string.IsNullOrWhiteSpace(role))
            q = q.Where(e => e.PrimaryRole == role);
        if (!string.IsNullOrWhiteSpace(engagement))
            q = q.Where(e => e.Engagement == engagement);
        if (!string.IsNullOrWhiteSpace(teamLead))
            q = q.Where(e => e.TeamLeadName == teamLead);
        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(e => e.Category == category);
        if (!active.HasValue || active.Value)
            q = q.Where(e => e.IsActive);

        var rows = await q.OrderBy(e => e.TeamLeadName).ThenBy(e => e.FullName).ToListAsync();
        return rows.Select(Map).ToList();
    }


    public async Task<EmployeeDto?> CreateAsync(CreateEmployeeDto dto)
    {
        var existing = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId);
        if (existing != null) return null;

        var emp = new Employee
        {
            EmployeeId   = dto.EmployeeId,
            FullName     = dto.FullName,
            FirstName    = dto.FullName.Split(' ').FirstOrDefault(),
            LastName     = dto.FullName.Split(' ').LastOrDefault(),
            Engagement   = dto.Engagement,
            PrimaryRole  = dto.PrimaryRole,
            TeamLeadName = dto.TeamLeadName,
            Category     = dto.Category,
            SourceSheet  = dto.SourceSheet ?? "GSD_DE",
            ShiftPattern = dto.ShiftPattern,
            IsActive     = true,
            CreatedAt    = DateTime.UtcNow
        };
        _db.Employees.Add(emp);
        await _db.SaveChangesAsync();
        return Map(emp);
    }

    public async Task<EmployeeDto?> UpdateAsync(string employeeId, UpdateEmployeeDto dto)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
        if (emp == null) return null;

        if (dto.FullName     != null) { emp.FullName = dto.FullName; emp.FirstName = dto.FullName.Split(' ').FirstOrDefault(); emp.LastName = dto.FullName.Split(' ').LastOrDefault(); }
        if (dto.Engagement   != null) emp.Engagement   = dto.Engagement;
        if (dto.PrimaryRole  != null) emp.PrimaryRole  = dto.PrimaryRole;
        if (dto.TeamLeadName != null) emp.TeamLeadName = dto.TeamLeadName;
        if (dto.Category     != null) emp.Category     = dto.Category;
        if (dto.IsActive.HasValue)    emp.IsActive      = dto.IsActive.Value;
        if (dto.Bundesland   != null) emp.Bundesland   = dto.Bundesland;
        if (dto.ShiftPattern != null) emp.ShiftPattern = dto.ShiftPattern;

        await _db.SaveChangesAsync();
        return Map(emp);
    }

    public async Task<bool> DeleteAsync(string employeeId)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
        if (emp == null) return false;
        emp.IsActive = false;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> GetFutureShiftCountAsync(string employeeId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return await _db.ShiftEntries
            .CountAsync(s => s.EmployeeId == employeeId && s.ShiftDate > today &&
                             s.ShiftType != "OFF" && s.ShiftType != "OFF_WEEKEND");
    }

    public async Task<EmployeeDto?> UpdateALBalanceAsync(string employeeId, int alUsed)
    {
        ALBalanceModel? bal = await _db.ALBalances.FirstOrDefaultAsync(b => b.EmployeeId == employeeId);
        if (bal == null)
        {
            var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
            if (emp == null) return null;
            bal = new ALBalanceModel { EmployeeId = employeeId, EmployeeName = emp.FullName, EligibleDays = 28 };
            _db.ALBalances.Add(bal);
        }
        bal.PlannedTakenAL = alUsed;
        bal.RemainingAL    = bal.EligibleDays - alUsed;
        bal.LastUpdated    = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await GetByIdAsync(employeeId);
    }

    public async Task<EmployeeDto?> GetByIdAsync(string employeeId)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.IsActive);
        return e == null ? null : Map(e);
    }

    public async Task<EmployeeTimelineDto?> GetTimelineAsync(string employeeId, string? from, string? to)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.IsActive);
        if (emp == null) return null;

        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : new DateOnly(DateTime.Today.Year, 1, 1);
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : new DateOnly(DateTime.Today.Year, 12, 31);

        var shifts = await _db.ShiftEntries
            .Where(s => s.EmployeeId == employeeId && s.ShiftDate >= fromDate && s.ShiftDate <= toDate)
            .OrderBy(s => s.ShiftDate)
            .Select(s => new ShiftTimelineItem(s.ShiftDate.ToString("yyyy-MM-dd"), s.ShiftType, s.ShiftStart, s.ShiftEnd, s.IsWicDuty))
            .ToListAsync();

        var sickLeaves = await _db.SickLeaves
            .Where(s => s.EmployeeId == employeeId)
            .OrderByDescending(s => s.FirstDay)
            .Select(s => new SickLeaveItem(s.FirstDay.ToString("yyyy-MM-dd"), s.LastDay.ToString("yyyy-MM-dd"), s.DurationDays, s.LeaveType))
            .ToListAsync();

        var vacations = await _db.Vacations
            .Where(v => v.EmployeeId == employeeId)
            .OrderByDescending(v => v.FirstDay)
            .Select(v => new VacationItem(v.FirstDay.ToString("yyyy-MM-dd"), v.LastDay.ToString("yyyy-MM-dd"), v.WorkDaysNet, v.Comments))
            .ToListAsync();

        return new EmployeeTimelineDto(employeeId, emp.FullName, emp.TeamLeadName, shifts, sickLeaves, vacations);
    }

    private static EmployeeDto Map(Employee e) => new(
        e.Id, e.EmployeeId, e.FirstName, e.LastName, e.FullName,
        e.Engagement, e.PrimaryRole, e.SecondaryRole,
        e.TeamLeadName, e.Category, e.IsActive, e.IsTrainee,
        e.PlannedRole, e.SourceSheet,
        e.Birthday.HasValue ? e.Birthday.Value.ToString("MM-dd") : null,
        e.Bundesland, e.ShiftPattern
    );
}


public record CreateEmployeeDto(
    string EmployeeId, string FullName, string? Engagement,
    string? PrimaryRole, string? TeamLeadName, string? Category, string? SourceSheet,
    string? ShiftPattern = null
);
public record UpdateEmployeeDto(
    string? FullName, string? Engagement, string? PrimaryRole,
    string? TeamLeadName, string? Category, bool? IsActive
, string? Bundesland, string? ShiftPattern = null);
public record ALBalanceUpdateDto(int AlUsed);

public static class EmployeeEndpointMapper
{
    public static void MapEmployeeEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/employees").WithTags("Employees");

        grp.MapGet("/", async (string? role, string? engagement, string? teamLead, string? category, bool? active, EmployeeService svc) =>
            Results.Ok(await svc.GetEmployeesAsync(role, engagement, teamLead, category, active)));

        grp.MapGet("/{employeeId}", async (string employeeId, EmployeeService svc) =>
        {
            var emp = await svc.GetByIdAsync(employeeId);
            return emp == null ? Results.NotFound() : Results.Ok(emp);
        });


        grp.MapPost("/", async (CreateEmployeeDto dto, EmployeeService svc) =>
        {
            var emp = await svc.CreateAsync(dto);
            return emp == null ? Results.Conflict("Employee ID already exists") : Results.Ok(emp);
        });

        grp.MapPatch("/{employeeId}", async (string employeeId, UpdateEmployeeDto dto, EmployeeService svc) =>
        {
            var emp = await svc.UpdateAsync(employeeId, dto);
            return emp == null ? Results.NotFound() : Results.Ok(emp);
        });

        grp.MapDelete("/{employeeId}", async (string employeeId, EmployeeService svc) =>
        {
            var count = await svc.GetFutureShiftCountAsync(employeeId);
            var ok = await svc.DeleteAsync(employeeId);
            return ok ? Results.Ok(new { deleted = true, futureShiftsAffected = count }) : Results.NotFound();
        });

        grp.MapGet("/{employeeId}/future-shifts", async (string employeeId, EmployeeService svc) =>
            Results.Ok(new { count = await svc.GetFutureShiftCountAsync(employeeId) }));

        grp.MapPatch("/{employeeId}/albalance", async (string employeeId, ALBalanceUpdateDto dto, EmployeeService svc) =>
        {
            var emp = await svc.UpdateALBalanceAsync(employeeId, dto.AlUsed);
            return emp == null ? Results.NotFound() : Results.Ok(emp);
        });

        grp.MapGet("/{employeeId}/timeline", async (string employeeId, string? from, string? to, EmployeeService svc) =>
        {
            var timeline = await svc.GetTimelineAsync(employeeId, from, to);
            return timeline == null ? Results.NotFound() : Results.Ok(timeline);
        });
    }
}




