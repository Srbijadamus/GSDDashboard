using GSDDashboard.API.Data;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.ALBalance;

public record ALBalanceDto(
    int Id, string? EmployeeId, string? EmployeeName, int Year,
    decimal EligibleDays, decimal PlannedTakenAL, decimal RemainingAL,
    int CountSL, int CountUL, int CountWorkingSundays, int CountFreeSundays
);

public class ALBalanceService
{
    private readonly GSDContext _db;
    public ALBalanceService(GSDContext db) => _db = db;

    // Real sick-leave types in SickLeaves (the "Self" / "SL" pair is self sick, "Child"
    // is child sick). Anything else stored in SickLeaves — 'AL', 'OFF', 'OL', 'UL', 'CD'
    // — is a data anomaly and is never counted as a sick day.
    private static readonly string[] SickLeaveTypes = { "Self", "SL", "Child" };

    public async Task<List<ALBalanceDto>> GetAllAsync(int? year = null)
    {
        var y = year ?? BerlinTime.Today.Year;
        var rows = await _db.ALBalances.Where(a => a.Year == y).OrderBy(a => a.EmployeeName).ToListAsync();
        var slDays = await ComputeSlDaysAsync(
            rows.Where(r => r.EmployeeId != null).Select(r => r.EmployeeId).Distinct().ToList(), y);
        return rows.Select(a => new ALBalanceDto(
            a.Id, a.EmployeeId, a.EmployeeName, a.Year,
            a.EligibleDays, a.PlannedTakenAL, a.RemainingAL,
            slDays.GetValueOrDefault(a.EmployeeId ?? ""),
            a.CountUL, a.CountWorkingSundays, a.CountFreeSundays
        )).ToList();
    }

    public async Task<ALBalanceDto?> GetByEmployeeAsync(string employeeId, int? year = null)
    {
        var y = year ?? BerlinTime.Today.Year;
        var a = await _db.ALBalances.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Year == y);
        if (a == null) return null;
        var slDays = await ComputeSlDaysAsync(new List<string?> { employeeId }, y);
        return new ALBalanceDto(
            a.Id, a.EmployeeId, a.EmployeeName, a.Year,
            a.EligibleDays, a.PlannedTakenAL, a.RemainingAL,
            slDays.GetValueOrDefault(employeeId),
            a.CountUL, a.CountWorkingSundays, a.CountFreeSundays
        );
    }

    /// <summary>
    /// SL days for the given employees and calendar year, computed from SickLeaves at
    /// read time: the sum of working days (Mon–Fri minus the employee's public holidays)
    /// of every real sick-leave row that overlaps the year, counting only the part of the
    /// period that lies inside the year. LeaveType 'AL' (and other non-sick types) are
    /// ignored; the ALBalance.CountSL column is not used here and remains untouched.
    /// </summary>
    private async Task<Dictionary<string, int>> ComputeSlDaysAsync(List<string?> empIds, int year)
    {
        var result = new Dictionary<string, int>();
        if (empIds.Count == 0) return result;

        var from = new DateOnly(year, 1, 1);
        var to   = new DateOnly(year, 12, 31);

        var leaves = (await _db.SickLeaves
                .Where(s => s.EmployeeId != null && empIds.Contains(s.EmployeeId)
                         && s.FirstDay <= to && s.LastDay >= from)
                .ToListAsync())
            .Where(s => Array.Exists(SickLeaveTypes, t => string.Equals(s.LeaveType, t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (leaves.Count == 0) return result;

        var leaveEmpIds = leaves.Select(s => s.EmployeeId).Distinct().ToList();
        var emps = await _db.Employees
            .Where(e => leaveEmpIds.Contains(e.EmployeeId))
            .ToDictionaryAsync(e => e.EmployeeId, e => e);

        // Same country rule as VacationService.DetermineCountryAsync: NL when the employee
        // has an active WIC assignment on an NL location, DE otherwise.
        var nlNames = (await _db.WicAgentAssignments
                .Where(a => a.IsActive && a.LocationCode.StartsWith("NL"))
                .Select(a => a.EmployeeName)
                .ToListAsync())
            .Where(n => n != null)
            .ToHashSet();

        var holidays = await _db.PublicHolidays
            .Where(h => h.HolidayDate >= from && h.HolidayDate <= to)
            .ToListAsync();

        foreach (var s in leaves)
        {
            var empId = s.EmployeeId;
            if (empId == null || !emps.TryGetValue(empId, out var emp)) continue;
            var country = emp.FullName != null && nlNames.Contains(emp.FullName) ? "NL" : "DE";
            var start = s.FirstDay < from ? from : s.FirstDay;
            var end   = s.LastDay > to ? to : s.LastDay;
            result[empId] = result.GetValueOrDefault(empId) + WorkingDays.Count(holidays, country, emp.Bundesland, start, end);
        }
        return result;
    }
}

public static class ALBalanceEndpointMapper
{
    public static void MapALBalanceEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/albalance").WithTags("ALBalance");

        grp.MapGet("/", async (int? year, ALBalanceService svc) =>
            Results.Ok(await svc.GetAllAsync(year)));

        grp.MapGet("/{employeeId}", async (string employeeId, int? year, ALBalanceService svc) =>
        {
            var bal = await svc.GetByEmployeeAsync(employeeId, year);
            return bal == null ? Results.NotFound() : Results.Ok(bal);
        });
    }
}
