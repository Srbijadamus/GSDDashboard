using GSDDashboard.API.Data;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Admin;

public record DemoDataDeleteResultDto(Dictionary<string, int> DeletedByTable, int TotalDeleted);

/// <summary>
/// Deletes DEMO test data (SourceModule = 'DEMO' or EmployeeId LIKE 'DEMO-%') from every base table
/// that has an EmployeeId column. Tables are discovered at runtime via INFORMATION_SCHEMA — never
/// hardcoded — so this stays correct as the schema evolves.
/// EN: Removes all DEMO test rows. DE: Entfernt alle DEMO-Testdaten.
/// </summary>
public class DemoDataAdminService
{
    private readonly GSDContext _db;
    private readonly ILogger<DemoDataAdminService> _log;

    public DemoDataAdminService(GSDContext db, ILogger<DemoDataAdminService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<DemoDataDeleteResultDto> DeleteDemoDataAsync()
    {
        // Discover every base table with an EmployeeId column at runtime — never hardcode table names.
        var employeeIdTables = await _db.Database
            .SqlQueryRaw<string>("""
                SELECT c.TABLE_NAME
                FROM INFORMATION_SCHEMA.COLUMNS c
                JOIN INFORMATION_SCHEMA.TABLES t
                  ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_TYPE = 'BASE TABLE'
                WHERE c.COLUMN_NAME = 'EmployeeId'
                """)
            .ToListAsync();

        // Tables that also carry a SourceModule column get the broader OR condition.
        var sourceModuleTables = (await _db.Database
                .SqlQueryRaw<string>("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME = 'SourceModule'")
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deletedByTable = new Dictionary<string, int>();

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Employees last: no FK constraints reference EmployeeId today (verified), but keeping
                // the natural-key table last is defensive if that ever changes.
                foreach (var table in employeeIdTables.OrderBy(t => string.Equals(t, "Employees", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                {
                    var quotedTable = "[" + table.Replace("]", "]]") + "]";
                    var sql = sourceModuleTables.Contains(table)
                        ? $"DELETE FROM {quotedTable} WHERE SourceModule = 'DEMO' OR EmployeeId LIKE 'DEMO-%'"
                        : $"DELETE FROM {quotedTable} WHERE EmployeeId LIKE 'DEMO-%'";

                    var deleted = await _db.Database.ExecuteSqlRawAsync(sql);
                    deletedByTable[table] = deleted;
                }

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _log.LogError(ex, "DeleteDemoDataAsync failed — rolled back");
                throw;
            }
        });

        return new DemoDataDeleteResultDto(deletedByTable, deletedByTable.Values.Sum());
    }
}

public static class DemoDataAdminEndpointMapper
{
    public static void MapDemoDataAdminEndpoints(this WebApplication app)
    {
        // EN: Deletes all DEMO test data across every table with an EmployeeId column.
        // DE: Loescht alle DEMO-Testdaten aus jeder Tabelle mit einer EmployeeId-Spalte.
        app.MapDelete("/api/admin/demo-data", async (DemoDataAdminService svc) =>
            Results.Ok(await svc.DeleteDemoDataAsync()))
           .WithTags("Admin");
    }
}
