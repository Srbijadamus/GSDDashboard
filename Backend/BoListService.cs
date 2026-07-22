using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.BoList;

public record BoEntryDto(int Id, string EmployeeName, string ShiftStart, string ShiftEnd, string? Note, int SortOrder);
public record BoEntryCreateDto(string Date, string EmployeeName, string ShiftStart, string ShiftEnd, string? Note);
public record BoEntryUpdateDto(string ShiftStart, string ShiftEnd, string? Note);

public class BoListService
{
    private readonly GSDContext _db;
    public BoListService(GSDContext db) => _db = db;

    public async Task<List<BoEntryDto>> GetByDateAsync(DateOnly date) =>
        await _db.BoEntries
            .Where(x => x.EntryDate == date)
            .OrderBy(x => x.SortOrder)
            .Select(x => new BoEntryDto(x.Id, x.EmployeeName, x.ShiftStart, x.ShiftEnd, x.Note, x.SortOrder))
            .ToListAsync();

    public async Task<BoEntryDto> CreateAsync(BoEntryCreateDto dto)
    {
        var date = DateOnly.Parse(dto.Date);
        var maxOrder = await _db.BoEntries
            .Where(x => x.EntryDate == date)
            .MaxAsync(x => (int?)x.SortOrder) ?? 0;

        var entry = new BoEntry
        {
            EntryDate    = date,
            EmployeeName = dto.EmployeeName,
            ShiftStart   = dto.ShiftStart,
            ShiftEnd     = dto.ShiftEnd,
            Note         = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            SortOrder    = maxOrder + 1
        };
        _db.BoEntries.Add(entry);
        await _db.SaveChangesAsync();
        return new BoEntryDto(entry.Id, entry.EmployeeName, entry.ShiftStart, entry.ShiftEnd, entry.Note, entry.SortOrder);
    }

    public async Task<BoEntryDto?> UpdateAsync(int id, BoEntryUpdateDto dto)
    {
        var entry = await _db.BoEntries.FindAsync(id);
        if (entry == null) return null;
        entry.ShiftStart = dto.ShiftStart;
        entry.ShiftEnd   = dto.ShiftEnd;
        entry.Note       = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        await _db.SaveChangesAsync();
        return new BoEntryDto(entry.Id, entry.EmployeeName, entry.ShiftStart, entry.ShiftEnd, entry.Note, entry.SortOrder);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entry = await _db.BoEntries.FindAsync(id);
        if (entry == null) return false;
        _db.BoEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return true;
    }
}

public static class BoListEndpointMapper
{
    public static void MapBoListEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/bo-list").WithTags("BoList");

        grp.MapGet("/", async (string? date, BoListService svc) =>
        {
            var d = date != null ? DateOnly.Parse(date) : DateOnly.FromDateTime(DateTime.Today);
            return Results.Ok(await svc.GetByDateAsync(d));
        });

        grp.MapPost("/", async (BoEntryCreateDto dto, BoListService svc) =>
            Results.Ok(await svc.CreateAsync(dto)));

        grp.MapPatch("/{id:int}", async (int id, BoEntryUpdateDto dto, BoListService svc) =>
        {
            var result = await svc.UpdateAsync(id, dto);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        grp.MapDelete("/{id:int}", async (int id, BoListService svc) =>
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.NoContent() : Results.NotFound();
        });
    }
}
