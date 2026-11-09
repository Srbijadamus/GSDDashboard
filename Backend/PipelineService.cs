using System.Globalization;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Modules.Pipeline;

public record PipelineDto(
    int Id, string? LocationCode, string? LocationName,
    string PipelineDate, string? PipelineDateEnd,
    string? Title, string? Description,
    string? PrimaryAgent, string? SecondAgent, string? BackupAgent,
    int AdditionalAgentsNeeded, string? HandledBy, string? HandlingType, string? CreatedBy,
    string Status, string? StartTime, string? EndTime, int AgentsRequired,
    string CreatedAt
);

public record CreatePipelineRequest(
    string? LocationCode, string PipelineDate, string? PipelineDateEnd,
    string? Title, string? Description, string? PrimaryAgent, string? SecondAgent, string? BackupAgent,
    int AdditionalAgentsNeeded, string? HandledBy, string? HandlingType,
    string? StartTime, string? EndTime, int AgentsRequired
);

public record UpdatePipelineRequest(
    string? Title, string? Description, string? PipelineDate, string? PipelineDateEnd,
    string? PrimaryAgent, string? SecondAgent, string? BackupAgent, int? AdditionalAgentsNeeded,
    string? HandledBy, string? HandlingType, string? Status, string? StartTime, string? EndTime,
    int? AgentsRequired, string? LocationCode
);

public sealed record PipelineSaveResult(PipelineDto? Dto, string? Error, bool NotFound = false);

public class PipelineService
{
    private static readonly string[] Statuses = { "PLANNED", "CONFIRMED", "CANCELLED" };
    private static readonly string[] HandlingTypes = { "ADDITIONAL", "LOCAL" };

    private readonly GSDContext _db;
    public PipelineService(GSDContext db) => _db = db;

    private static PipelineDto Map(WicPipelineItem p, string? locationName) =>
        new(p.Id, p.LocationCode, locationName,
            p.PipelineDate.ToString("yyyy-MM-dd"),
            p.PipelineDateEnd?.ToString("yyyy-MM-dd"),
            p.Title, p.Description, p.PrimaryAgent, p.SecondAgent, p.BackupAgent,
            p.AdditionalAgentsNeeded, p.HandledBy, p.HandlingType, p.CreatedBy,
            p.Status, p.StartTime, p.EndTime, p.AgentsRequired,
            // CreatedAt is stored UTC; the API exposes it as Berlin wall clock (BerlinTime).
            BerlinTime.FromUtc(p.CreatedAt).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

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

    public async Task<PipelineSaveResult> CreateAsync(CreatePipelineRequest req)
    {
        var err = ValidateCreate(req);
        if (err != null) return new(null, err);

        DateOnly pd = DateOnly.Parse(req.PipelineDate);
        DateOnly? pde = string.IsNullOrWhiteSpace(req.PipelineDateEnd) ? null : DateOnly.Parse(req.PipelineDateEnd);

        var item = new WicPipelineItem
        {
            LocationCode = req.LocationCode?.Trim(),
            PipelineDate = pd,
            PipelineDateEnd = pde,
            Title = req.Title?.Trim(),
            Description = req.Description,
            PrimaryAgent = NullIfBlank(req.PrimaryAgent),
            SecondAgent = NullIfBlank(req.SecondAgent),
            BackupAgent = NullIfBlank(req.BackupAgent),
            AdditionalAgentsNeeded = req.AdditionalAgentsNeeded,
            HandledBy = req.HandledBy,
            HandlingType = string.IsNullOrWhiteSpace(req.HandlingType) ? "ADDITIONAL" : req.HandlingType!.Trim(),
            StartTime = NullIfBlank(req.StartTime),
            EndTime = NullIfBlank(req.EndTime),
            AgentsRequired = req.AgentsRequired,
            Status = "PLANNED",
            CreatedAt = DateTime.UtcNow
        };
        _db.WicPipeline.Add(item);
        await _db.SaveChangesAsync();
        return new((await GetByIdAsync(item.Id))!, null);
    }

    public async Task<PipelineSaveResult> UpdateAsync(int id, UpdatePipelineRequest req)
    {
        var item = await _db.WicPipeline.FindAsync(id);
        if (item == null) return new(null, null, NotFound: true);

        var err = ValidateUpdate(item, req);
        if (err != null) return new(null, err);

        if (req.Title       != null) item.Title       = req.Title.Trim();
        if (req.Description != null) item.Description = req.Description;
        if (req.PrimaryAgent   != null) item.PrimaryAgent   = NullIfBlank(req.PrimaryAgent);
        if (req.SecondAgent    != null) item.SecondAgent    = NullIfBlank(req.SecondAgent);
        if (req.BackupAgent    != null) item.BackupAgent    = NullIfBlank(req.BackupAgent);
        if (req.HandledBy      != null) item.HandledBy      = req.HandledBy;
        if (req.HandlingType   != null) item.HandlingType   = req.HandlingType.Trim();
        if (req.Status         != null) item.Status         = req.Status;
        if (req.StartTime      != null) item.StartTime      = NullIfBlank(req.StartTime);
        if (req.EndTime        != null) item.EndTime        = NullIfBlank(req.EndTime);
        if (req.LocationCode   != null) item.LocationCode   = req.LocationCode.Trim();
        if (req.AdditionalAgentsNeeded.HasValue) item.AdditionalAgentsNeeded = req.AdditionalAgentsNeeded.Value;
        if (req.AgentsRequired.HasValue) item.AgentsRequired = req.AgentsRequired.Value;
        if (req.PipelineDate    != null && DateOnly.TryParse(req.PipelineDate,    out var pd))  item.PipelineDate    = pd;
        if (req.PipelineDateEnd != null)
        {
            item.PipelineDateEnd = string.IsNullOrWhiteSpace(req.PipelineDateEnd)
                ? null
                : (DateOnly.TryParse(req.PipelineDateEnd, out var pde) ? pde : item.PipelineDateEnd);
        }

        await _db.SaveChangesAsync();
        return new(await GetByIdAsync(id), null);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _db.WicPipeline.FindAsync(id);
        if (item == null) return false;
        _db.WicPipeline.Remove(item);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? ValidateTimes(string? start, string? end)
    {
        var hasS = !string.IsNullOrWhiteSpace(start);
        var hasE = !string.IsNullOrWhiteSpace(end);
        TimeOnly s = default, e = default;
        if (hasS && !TimeOnly.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out s))
            return "StartTime must be a valid time (HH:mm).";
        if (hasE && !TimeOnly.TryParseExact(end, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out e))
            return "EndTime must be a valid time (HH:mm).";
        if (hasS && hasE && e <= s)
            return "EndTime must be later than StartTime.";
        return null;
    }

    private static string? DuplicateAgentError(string? primary, string? second, string? backup)
    {
        var names = new[] { primary, second, backup }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .ToList();
        for (var i = 0; i < names.Count; i++)
            for (var j = i + 1; j < names.Count; j++)
                if (string.Equals(names[i], names[j], StringComparison.OrdinalIgnoreCase))
                    return "The same agent cannot be assigned more than once (Primary, Second, Backup).";
        return null;
    }

    private static string? ValidateCreate(CreatePipelineRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return "Title is required.";

        if (string.IsNullOrWhiteSpace(req.PipelineDate) || !DateOnly.TryParse(req.PipelineDate, out var pd))
            return "PipelineDate (start date) is required and must be a valid date.";

        if (!string.IsNullOrWhiteSpace(req.PipelineDateEnd))
        {
            if (!DateOnly.TryParse(req.PipelineDateEnd, out var d))
                return "PipelineDateEnd must be a valid date.";
            if (d < pd)
                return "PipelineDateEnd must not be before PipelineDate.";
        }

        var tErr = ValidateTimes(req.StartTime, req.EndTime);
        if (tErr != null) return tErr;

        if (string.IsNullOrWhiteSpace(req.LocationCode))
            return "Location is required.";

        if (req.HandlingType != null && !HandlingTypes.Contains(req.HandlingType.Trim(), StringComparer.OrdinalIgnoreCase))
            return "HandlingType must be ADDITIONAL or LOCAL.";

        if (req.AgentsRequired < 1)
            return "AgentsRequired must be at least 1.";

        return DuplicateAgentError(req.PrimaryAgent, req.SecondAgent, req.BackupAgent);
    }

    private static string? ValidateUpdate(WicPipelineItem item, UpdatePipelineRequest req)
    {
        var title = req.Title != null ? req.Title.Trim() : item.Title;
        if (string.IsNullOrWhiteSpace(title))
            return "Title is required.";

        DateOnly pd = item.PipelineDate;
        if (req.PipelineDate != null)
        {
            if (!DateOnly.TryParse(req.PipelineDate, out var npd))
                return "PipelineDate must be a valid date.";
            pd = npd;
        }

        DateOnly? pde = item.PipelineDateEnd;
        if (req.PipelineDateEnd != null)
        {
            if (string.IsNullOrWhiteSpace(req.PipelineDateEnd)) pde = null;
            else
            {
                if (!DateOnly.TryParse(req.PipelineDateEnd, out var npde))
                    return "PipelineDateEnd must be a valid date.";
                pde = npde;
            }
        }
        if (pde != null && pde < pd)
            return "PipelineDateEnd must not be before PipelineDate.";

        var start = req.StartTime != null ? req.StartTime : item.StartTime;
        var end   = req.EndTime   != null ? req.EndTime   : item.EndTime;
        var tErr = ValidateTimes(start, end);
        if (tErr != null) return tErr;

        var loc = req.LocationCode != null ? req.LocationCode : item.LocationCode;
        if (string.IsNullOrWhiteSpace(loc))
            return "Location is required.";

        var status = req.Status != null ? req.Status : item.Status;
        if (!Statuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            return "Status must be PLANNED, CONFIRMED or CANCELLED.";

        var handling = req.HandlingType != null ? req.HandlingType : item.HandlingType;
        if (handling != null && !HandlingTypes.Contains(handling.Trim(), StringComparer.OrdinalIgnoreCase))
            return "HandlingType must be ADDITIONAL or LOCAL.";

        var agentsRequired = req.AgentsRequired ?? item.AgentsRequired;
        if (agentsRequired < 1)
            return "AgentsRequired must be at least 1.";

        var primary = req.PrimaryAgent != null ? req.PrimaryAgent : item.PrimaryAgent;
        var second  = req.SecondAgent  != null ? req.SecondAgent  : item.SecondAgent;
        var backup  = req.BackupAgent  != null ? req.BackupAgent  : item.BackupAgent;
        return DuplicateAgentError(primary, second, backup);
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
            var res = await svc.CreateAsync(req);
            if (res.Error != null) return Results.BadRequest(new { error = res.Error });
            return Results.Created($"/api/pipeline/{res.Dto!.Id}", res.Dto);
        });

        grp.MapPatch("/{id:int}", async (int id, UpdatePipelineRequest req, PipelineService svc) =>
        {
            var res = await svc.UpdateAsync(id, req);
            if (res.NotFound) return Results.NotFound();
            if (res.Error != null) return Results.BadRequest(new { error = res.Error });
            return Results.Ok(res.Dto);
        });

        grp.MapDelete("/{id:int}", async (int id, PipelineService svc) =>
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.Ok(new { deleted = true }) : Results.NotFound();
        });
    }
}
