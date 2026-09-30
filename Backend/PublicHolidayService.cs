using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
namespace GSDDashboard.API.Modules.PublicHolidays;

public record PublicHolidayDto(int Id, string Date, string Name, string? Bundesland, bool IsNational);

public class PublicHolidayService
{
    private readonly GSDContext _db;
    public PublicHolidayService(GSDContext db) => _db = db;

    public async Task<List<PublicHolidayDto>> GetHolidaysAsync(string? bundesland, int? year)
    {
        var y = year ?? DateTime.Today.Year;
        // Filtered in-memory via PublicHolidayResolver.Applies (Mojibake-
        // resistant) rather than a SQL-translated `==` — see Mojibake.cs.
        var yearRows = await _db.PublicHolidays.Where(h => h.HolidayDate.Year == y).ToListAsync();
        var rows = yearRows.Where(h => PublicHolidayResolver.Applies(h, bundesland))
            .OrderBy(h => h.HolidayDate).ToList();
        return rows.Select(h => new PublicHolidayDto(h.Id, h.HolidayDate.ToString("yyyy-MM-dd"), h.Name, h.Bundesland, h.IsNational)).ToList();
    }

    public async Task<List<PublicHolidayDto>> GetAgentHolidaysAsync(string employeeId, string from, string to)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
        if (emp == null) return new();
        var fromDate = DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
        var toDate   = DateOnly.TryParse(to,   out var td) ? td : fromDate.AddDays(30);
        var rangeRows = await _db.PublicHolidays.Where(h => h.HolidayDate >= fromDate && h.HolidayDate <= toDate).ToListAsync();
        var rows = rangeRows.Where(h => PublicHolidayResolver.Applies(h, emp.Bundesland))
            .OrderBy(h => h.HolidayDate).ToList();
        return rows.Select(h => new PublicHolidayDto(h.Id, h.HolidayDate.ToString("yyyy-MM-dd"), h.Name, h.Bundesland, h.IsNational)).ToList();
    }
}

public static class PublicHolidayEndpointMapper
{
    public static void MapPublicHolidayEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/publicholidays").WithTags("PublicHolidays");

        grp.MapGet("/", async (string? bundesland, int? year, PublicHolidayService svc) =>
            Results.Ok(await svc.GetHolidaysAsync(bundesland, year)));

        grp.MapGet("/agent/{employeeId}", async (string employeeId, string? from, string? to, PublicHolidayService svc) =>
            Results.Ok(await svc.GetAgentHolidaysAsync(employeeId, from ?? DateTime.Today.ToString("yyyy-MM-dd"), to ?? DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"))));

        grp.MapGet("/bundeslaender", () => Results.Ok(new[] {
            "Bayern","NRW","Hamburg","Berlin","Baden-Württemberg","Sachsen","Thüringen",
            "Brandenburg","Sachsen-Anhalt","Mecklenburg-Vorpommern","Niedersachsen",
            "Bremen","Hessen","Rheinland-Pfalz","Saarland","Schleswig-Holstein"
        }));
    }
}
