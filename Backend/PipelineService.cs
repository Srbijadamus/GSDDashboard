using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Pipeline;

public record PipelineDto(
    int Id, string? LocationCode, string? LocationName,
    string PipelineDate, string? PipelineDateEnd,
    string? Title, string? Description,
    string? PrimaryAgent, string? BackupAgent,
    int AdditionalAgentsNeeded, string? HandledBy, string? CreatedBy,
    string Status, string? StartTime, string? EndTime, int AgentsRequired,
    string CreatedAt
);

public record CreatePipelineRequest(
    string? LocationCode, string PipelineDate, string? PipelineDateEnd,
    string? Title, string? Description, string? PrimaryAgent, string? BackupAgent,
    int AdditionalAgentsNeeded, string? HandledBy, string? StartTime, string? EndTime, int AgentsRequired
);

public record UpdatePipelineRequest(
    string? Title, string? Description, string? PipelineDate, string? PipelineDateEnd,
    string? PrimaryAgent, string? BackupAgent, int? AdditionalAgentsNeeded,
    string? HandledBy, string? Status, string? StartTime, string? EndTime,
    int? AgentsRequired, string? LocationCode
);

public class PipelineService
{
    private readonly GSDContext _db;
    public PipelineService(GSDContext db) => _db = db;

    private static PipelineDto Map(WicPipelineItem p, string? locationName) =>
        new(p.Id, p.LocationCode, locationName,
            p.PipelineDate.ToString("yyyy-MM-dd"),
            p.PipelineDateEnd?.ToString("yyyy-MM-dd"),
            p.Title, p.Description, p.PrimaryAgent, p.BackupAgent,
            p.AdditionalAgentsNeeded, p.HandledBy, p.CreatedBy,
            p.Status, p.StartTime, p.EndTime, p.AgentsRequired,
            p.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"));

    public async Task<List<PipelineDto>> GetAllAsync(string? from, string? to, string? locationCode)
    {
        var fromDate = from != null && DateOnly.TryParse(from, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);
        var toDate   = to   != null && DateOnly.TryParse(to,   out var td) ? td : fromDate.AddDays(30);

        var q = _db.WicPipeline.Where(p =>
            p.PipelineDate <= toDate &&
            (p.PipelineDateEnd ?? p.PipelineDate) >= fromDate);

        if (!string.IsNullOrWhiteSpace(locationCode))
            q = q.Where(p => p.LocationCode == locationCode);

        var items = await q.OrderBy(p => p.PipelineDate).ToListAsync();

        var codes = items.Where(i => i.LocationCode != null).Select(i => i.LocationCode!).Distinct().ToList();
        var locations = await _db.WicLocations
            .Where(l => codes.Contains(l.LocationCode))
            .ToDictionaryAsync(l => l.LocationCode, l => l.DisplayName ?? l.LocationCode);

        return items.Select(p => Map(p,
            p.LocationCode != null && locations.ContainsKey(p.LocationCode)
                ? locations[p.LocationCode] : p.LocationCode)).ToList();
    }

    public async Task<PipelineDto?> GetByIdAsync(int id)
    {
        var p = await _db.WicPipeline.FindAsync(id);
        if (p == null) return null;
        string? locName = null;
        if (p.LocationCode != null)
        {
            var loc = await _db.WicLocations.FirstOrDefaultAsync(l => l.LocationCode == p.LocationCode);
            locName = loc?.DisplayName ?? p.LocationCode;
        }
        return Map(p, locName);
    }

    public async Task<PipelineDto> CreateAsync(CreatePipelineRequest req)
    {
        if (!DateOnly.TryParse(req.PipelineDate, out var pd)) pd = DateOnly.FromDateTime(DateTime.Today);
        DateOnly? pde = null;
        if (req.PipelineDateEnd != null && DateOnly.TryParse(req.PipelineDateEnd, out var pded)) pde = pded;

        var item = new WicPipelineItem
        {
            LocationCode = req.LocationCode, PipelineDate = pd, PipelineDateEnd = pde,
            Title = req.Title, Description = req.Description,
            PrimaryAgent = req.PrimaryAgent, BackupAgent = req.BackupAgent,
            AdditionalAgentsNeeded = req.AdditionalAgentsNeeded, HandledBy = req.HandledBy,
            StartTime = req.StartTime, EndTime = req.EndTime,
            AgentsRequired = req.AgentsRequired, Status = "PLANNED", CreatedAt = DateTime.UtcNow
        };
        _db.WicPipeline.Add(item);
        await _db.SaveChangesAsync();
        return (await GetByIdAsync(item.Id))!;
    }

    public async Task<PipelineDto?> UpdateAsync(int id, UpdatePipelineRequest req)
    {
        var item = await _db.WicPipeline.FindAsync(id);
        if (item == null) return null;

        if (req.Title       != null) item.Title       = req.Title;
        if (req.Description != null) item.Description = req.Description;
        if (req.PrimaryAgent   != null) item.PrimaryAgent   = req.PrimaryAgent;
        if (req.BackupAgent    != null) item.BackupAgent    = req.BackupAgent;
        if (req.HandledBy      != null) item.HandledBy      = req.HandledBy;
        if (req.Status         != null) item.Status         = req.Status;
        if (req.StartTime      != null) item.StartTime      = req.StartTime;
        if (req.EndTime        != null) item.EndTime        = req.EndTime;
        if (req.LocationCode   != null) item.LocationCode   = req.LocationCode;
        if (req.AdditionalAgentsNeeded.HasValue) item.AdditionalAgentsNeeded = req.AdditionalAgentsNeeded.Value;
        if (req.AgentsRequired.HasValue) item.AgentsRequired = req.AgentsRequired.Value;
        if (req.PipelineDate    != null && DateOnly.TryParse(req.PipelineDate,    out var pd))  item.PipelineDate    = pd;
        if (req.PipelineDateEnd != null && DateOnly.TryParse(req.PipelineDateEnd, out var pde)) item.PipelineDateEnd = pde;

        await _db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _db.WicPipeline.FindAsync(id);
        if (item == null) return false;
        _db.WicPipeline.Remove(item);
        await _db.SaveChangesAsync();
        return true;
    }
}

public static class PipelineEndpointMapper
{
    public static void MapPipelineEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/pipeline").WithTags("Pipeline");

        grp.MapGet("/", async (string? from, string? to, string? locationCode, PipelineService svc) =>
            Results.Ok(await svc.GetAllAsync(from, to, locationCode)));

        grp.MapGet("/{id:int}", async (int id, PipelineService svc) =>
        {
            var r = await svc.GetByIdAsync(id);
            return r == null ? Results.NotFound() : Results.Ok(r);
        });

        grp.MapPost("/", async (CreatePipelineRequest req, PipelineService svc) =>
        {
            var r = await svc.CreateAsync(req);
            return Results.Created($"/api/pipeline/{r.Id}", r);
        });

        grp.MapPatch("/{id:int}", async (int id, UpdatePipelineRequest req, PipelineService svc) =>
        {
            var r = await svc.UpdateAsync(id, req);
            return r == null ? Results.NotFound() : Results.Ok(r);
        });

        grp.MapDelete("/{id:int}", async (int id, PipelineService svc) =>
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.Ok(new { deleted = true }) : Results.NotFound();
        });
    }
}
