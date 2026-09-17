using GSDDashboard.API.Data;
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

    public async Task<List<ALBalanceDto>> GetAllAsync(int? year = null)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var rows = await _db.ALBalances.Where(a => a.Year == y).OrderBy(a => a.EmployeeName).ToListAsync();
        return rows.Select(a => new ALBalanceDto(
            a.Id, a.EmployeeId, a.EmployeeName, a.Year,
            a.EligibleDays, a.PlannedTakenAL, a.RemainingAL,
            a.CountSL, a.CountUL, a.CountWorkingSundays, a.CountFreeSundays
        )).ToList();
    }

    public async Task<ALBalanceDto?> GetByEmployeeAsync(string employeeId, int? year = null)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var a = await _db.ALBalances.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Year == y);
        if (a == null) return null;
        return new ALBalanceDto(
            a.Id, a.EmployeeId, a.EmployeeName, a.Year,
            a.EligibleDays, a.PlannedTakenAL, a.RemainingAL,
            a.CountSL, a.CountUL, a.CountWorkingSundays, a.CountFreeSundays
        );
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

