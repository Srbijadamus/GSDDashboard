using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.BulkRtm;

public record RtmEntryDto(
    int Id, string EmployeeId, string? FullName,
    string ShiftStart, string ShiftEnd, string? Tag,
    string? SourceLine, DateTime CreatedAt);

public record RtmSaveRowDto(
    string EmployeeId, string? FullName,
    string ShiftStart, string ShiftEnd,
    string? Tag, string? SourceLine);

public record RtmSaveDto(string Date, List<RtmSaveRowDto> Rows);

public record RtmManualAddDto(
    string EmployeeId, string? FullName,
    string ShiftStart, string ShiftEnd, string? Tag);

public record RtmRowUpdateDto(string? ShiftStart, string? ShiftEnd, string? Tag);

public record RtmSaveResult(int SavedCount, string Date);
public record HomeWicDto(string EmployeeId, string? LocationCode, string? DisplayName);

public class BulkRtmService
{
    private readonly GSDContext _db;
    public BulkRtmService(GSDContext db) => _db = db;

    public async Task<List<RtmEntryDto>> GetByDateAsync(DateOnly date) =>
        await _db.RtmEntries
            .Where(x => x.EntryDate == date)
            .OrderBy(x => x.Id)
            .Select(x => new RtmEntryDto(
                x.Id, x.EmployeeId, x.FullName,
                x.ShiftStart, x.ShiftEnd, x.Tag,
                x.SourceLine, x.CreatedAt))
            .ToListAsync();

    public async Task<RtmSaveResult> SaveAsync(RtmSaveDto dto)
    {
        var date = DateOnly.Parse(dto.Date);

        // Load and remove existing rows for the day
        var existing = await _db.RtmEntries
            .Where(x => x.EntryDate == date)
            .ToListAsync();
        _db.RtmEntries.RemoveRange(existing);

        foreach (var row in dto.Rows)
        {
            _db.RtmEntries.Add(new RtmEntry
            {
                EntryDate  = date,
                EmployeeId = row.EmployeeId,
                FullName   = string.IsNullOrWhiteSpace(row.FullName) ? null : row.FullName.Trim(),
                ShiftStart = row.ShiftStart,
                ShiftEnd   = row.ShiftEnd,
                Tag        = string.IsNullOrWhiteSpace(row.Tag) ? null : row.Tag.Trim(),
                SourceLine = string.IsNullOrWhiteSpace(row.SourceLine) ? null : row.SourceLine.Trim(),
                CreatedAt  = DateTime.UtcNow,
            });
        }

        // SaveChangesAsync wraps the delete+insert batch in an implicit transaction
        await _db.SaveChangesAsync();
        return new RtmSaveResult(dto.Rows.Count, dto.Date);
    }

    public async Task<RtmEntryDto> AddRowAsync(DateOnly date, RtmManualAddDto dto)
    {
        var entry = new RtmEntry
        {
            EntryDate  = date,
            EmployeeId = dto.EmployeeId,
            FullName   = string.IsNullOrWhiteSpace(dto.FullName) ? null : dto.FullName.Trim(),
            ShiftStart = dto.ShiftStart,
            ShiftEnd   = dto.ShiftEnd,
            Tag        = string.IsNullOrWhiteSpace(dto.Tag) ? null : dto.Tag.Trim(),
            CreatedAt  = DateTime.UtcNow,
        };
        _db.RtmEntries.Add(entry);
        await _db.SaveChangesAsync();
        return new RtmEntryDto(entry.Id, entry.EmployeeId, entry.FullName,
            entry.ShiftStart, entry.ShiftEnd, entry.Tag, entry.SourceLine, entry.CreatedAt);
    }

    public async Task<RtmEntryDto?> UpdateRowAsync(int id, RtmRowUpdateDto dto)
    {
        var entry = await _db.RtmEntries.FindAsync(id);
        if (entry == null) return null;
        if (dto.ShiftStart != null) entry.ShiftStart = dto.ShiftStart;
        if (dto.ShiftEnd   != null) entry.ShiftEnd   = dto.ShiftEnd;
        entry.Tag = string.IsNullOrWhiteSpace(dto.Tag) ? null : dto.Tag.Trim();
        await _db.SaveChangesAsync();
        return new RtmEntryDto(entry.Id, entry.EmployeeId, entry.FullName,
            entry.ShiftStart, entry.ShiftEnd, entry.Tag, entry.SourceLine, entry.CreatedAt);
    }

    public async Task<bool> DeleteRowAsync(int id)
    {
        var entry = await _db.RtmEntries.FindAsync(id);
        if (entry == null) return false;
        _db.RtmEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return true;
    }
}

public static class BulkRtmEndpointMapper
{
    public static void MapBulkRtmEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/rtm").WithTags("BulkRtm");

        grp.MapGet("/", async (string? date, BulkRtmService svc) =>
        {
            var d = date != null ? DateOnly.Parse(date) : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetByDateAsync(d));
        });

        grp.MapPost("/save", async (RtmSaveDto dto, BulkRtmService svc) =>
            Results.Ok(await svc.SaveAsync(dto)));

        grp.MapPost("/rows", async (string? date, RtmManualAddDto dto, BulkRtmService svc) =>
        {
            var d = date != null ? DateOnly.Parse(date) : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.AddRowAsync(d, dto));
        });

        grp.MapPatch("/rows/{id:int}", async (int id, RtmRowUpdateDto dto, BulkRtmService svc) =>
        {
            var result = await svc.UpdateRowAsync(id, dto);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        grp.MapDelete("/rows/{id:int}", async (int id, BulkRtmService svc) =>
        {
            var ok = await svc.DeleteRowAsync(id);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // Returns [{employeeId, locationCode, displayName}] for each requested ID.
        // Resolves MAIN WicAgentAssignment via EF Core queries (SQL Server collation handles
        // any encoding mismatches between WicAgentAssignments and WicLocations codes).
        grp.MapGet("/home-wic", async (string? ids, GSDContext db) =>
        {
            if (string.IsNullOrWhiteSpace(ids)) return Results.Ok(Array.Empty<HomeWicDto>());
            var idSet = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .ToHashSet();

            var emps = await db.Employees
                .Where(e => idSet.Contains(e.EmployeeId) && e.IsActive && e.FullName != null)
                .Select(e => new { e.EmployeeId, FullName = e.FullName! })
                .ToListAsync();

            if (emps.Count == 0) return Results.Ok(idSet.Select(id => new HomeWicDto(id, null, null)).ToList());

            var nameSet = emps.Select(e => e.FullName).ToHashSet();
            var assignments = await db.WicAgentAssignments
                .Where(a => a.IsActive && a.AssignmentType == "MAIN" && nameSet.Contains(a.EmployeeName))
                .Select(a => new { a.EmployeeName, a.LocationCode })
                .ToListAsync();

            // Resolve each distinct assignment code via EF Core (SQL Server collation handles
            // encoding inconsistencies between the two tables; returns null when unresolvable).
            var distinctCodes = assignments.Select(a => a.LocationCode).Distinct().ToList();
            var codeToLoc = new Dictionary<string, (string Code, string Name)?>();
            foreach (var code in distinctCodes)
            {
                var loc = await db.WicLocations
                    .Where(l => l.IsActive && (l.LocationCode == code || l.LocationCodeLegacy == code))
                    .Select(l => new { l.LocationCode, l.DisplayName })
                    .FirstOrDefaultAsync();
                codeToLoc[code] = loc != null ? (loc.LocationCode, loc.DisplayName) : ((string, string)?)null;
            }

            var result = emps.Select(e =>
            {
                var asgn = assignments.FirstOrDefault(a => a.EmployeeName == e.FullName);
                if (asgn == null) return new HomeWicDto(e.EmployeeId, null, null);
                var loc = codeToLoc.TryGetValue(asgn.LocationCode, out var v) ? v : null;
                return new HomeWicDto(e.EmployeeId, loc?.Code, loc?.Name);
            }).ToList();

            return Results.Ok(result);
        });
    }
}
