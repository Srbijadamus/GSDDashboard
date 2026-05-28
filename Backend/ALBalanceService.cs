using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.ALBalance;

public record ALBalanceDto(
    int Id, string? EmployeeId, string? EmployeeName,
    int EligibleDays, int PlannedTakenAL, int RemainingAL,
    int CountSL, int CountUL, int CountWorkingSundays, int CountFreeSundays
);

public class ALBalanceService
{
    private readonly GSDContext _db;
    public ALBalanceService(GSDContext db) => _db = db;

    public async Task<List<ALBalanceDto>> GetAllAsync()
    {
        var rows = await _db.ALBalances.OrderBy(a => a.EmployeeName).ToListAsync();
        return rows.Select(a => new ALBalanceDto(
            a.Id, a.EmployeeId, a.EmployeeName,
            a.EligibleDays, a.PlannedTakenAL, a.RemainingAL,
            a.CountSL, a.CountUL, a.CountWorkingSundays, a.CountFreeSundays
        )).ToList();
    }

    public async Task<ALBalanceDto?> GetByEmployeeAsync(string employeeId)
    {
        var a = await _db.ALBalances.FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        if (a == null) return null;
        return new ALBalanceDto(
            a.Id, a.EmployeeId, a.EmployeeName,
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

        grp.MapGet("/", async (ALBalanceService svc) =>
            Results.Ok(await svc.GetAllAsync()));

        grp.MapGet("/{employeeId}", async (string employeeId, ALBalanceService svc) =>
        {
            var bal = await svc.GetByEmployeeAsync(employeeId);
            return bal == null ? Results.NotFound() : Results.Ok(bal);
        });
    }
}
