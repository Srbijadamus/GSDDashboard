using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record AgentCoverageDto(
    string   EmployeeId,
    string?  PrimaryKid,
    string?  SecondaryKid,
    string?  FullName,
    string?  InfosysEmail,
    string?  EonEmail,
    bool?    HasCar,
    string?  GroupRegion,
    string[] ReachableCities,
    WicRoleDto[] WicRoles
);

public record WicRoleDto(string LocationCode, string DisplayName, string AssignmentType);

public record WicCoverageDto(
    string   LocationCode,
    string   DisplayName,
    string?  City,
    string?  Bundesland,
    string?  OpeningDay,
    string?  Comment,
    string?  FullAddress,
    AgentTierDto[] Main,
    AgentTierDto[] BackupA,
    AgentTierDto[] BackupB,
    AgentTierDto[] BackupC
);

public record AgentTierDto(string Name, bool? HasCar, string? EmployeeId, string? PrimaryKid);

public record WicListItemDto(
    string  LocationCode,
    string  DisplayName,
    string? City,
    string? Bundesland,
    string? OpeningDay,
    int     MainCount,
    int     BackupACount
);

public record PatchAgentDto(bool? HasCar, string? GroupRegion);
public record PinBackupBDto(string EmployeeName);

// ─── Service ──────────────────────────────────────────────────────────────────

public class WicCoverageService(GSDContext db)
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { "Ferenc Koreh", "Tunde Szabo", "Zsolt Fulop" };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsExcluded(string name) => Excluded.Contains(name.Trim());

    // Derive base city from DisplayName: "Essen - TK" → "Essen", "Hamburg" → "Hamburg".
    // Split on " - " (with spaces) so "Neu-Isenburg" and "s-Hertogenbosch" are kept intact.
    private static string? BaseCityOf(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        var sep = displayName.IndexOf(" - ", StringComparison.Ordinal);
        return sep >= 0 ? displayName[..sep].Trim() : displayName.Trim();
    }

    private static AgentTierDto ToTierDto(string name, ILookup<string, Employee> byName)
    {
        var emp = byName[name.Trim().ToLowerInvariant()].FirstOrDefault();
        return new AgentTierDto(name, emp?.HasCar, emp?.EmployeeId, emp?.PrimaryKid);
    }

    // ── Agents ────────────────────────────────────────────────────────────────

    public async Task<List<AgentCoverageDto>> GetAgentsAsync(string? search)
    {
        var coveredIds = await db.AgentReachableCities
            .Where(c => c.EmployeeId != null)
            .Select(c => c.EmployeeId!)
            .Distinct()
            .ToListAsync();

        var employees = (await db.Employees
            .Where(e => e.PrimaryKid != null || coveredIds.Contains(e.EmployeeId))
            .ToListAsync())
            .Where(e => !IsExcluded(e.FullName ?? ""))
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            employees = employees
                .Where(e => (e.FullName ?? "").ToLowerInvariant().Contains(s)
                         || (e.PrimaryKid ?? "").ToLowerInvariant().Contains(s)
                         || (e.GroupRegion ?? "").ToLowerInvariant().Contains(s))
                .ToList();
        }

        var allCities = await db.AgentReachableCities.ToListAsync();
        var allAssignments = await db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();
        var allWics = await db.WicLocations.ToListAsync();
        var wicByCode = allWics.ToDictionary(w => w.LocationCode, StringComparer.OrdinalIgnoreCase);

        return employees
            .OrderBy(e => e.FullName)
            .Select(e =>
            {
                var cities = allCities
                    .Where(c => c.EmployeeId == e.EmployeeId)
                    .Select(c => c.City)
                    .Distinct()
                    .Order()
                    .ToArray();

                var roles = allAssignments
                    .Where(a => string.Equals(a.EmployeeName, e.FullName, StringComparison.OrdinalIgnoreCase))
                    .Select(a => new WicRoleDto(
                        a.LocationCode,
                        wicByCode.TryGetValue(a.LocationCode, out var w) ? w.DisplayName : a.LocationCode,
                        a.AssignmentType))
                    .ToArray();

                return new AgentCoverageDto(
                    e.EmployeeId, e.PrimaryKid, e.SecondaryKid, e.FullName,
                    e.InfosysEmail, e.EonEmail, e.HasCar, e.GroupRegion,
                    cities, roles);
            })
            .ToList();
    }

    public async Task<AgentCoverageDto?> GetAgentByKidAsync(string kid)
    {
        var emp = await db.Employees
            .FirstOrDefaultAsync(e => e.PrimaryKid == kid || e.EmployeeId == kid);
        if (emp == null || IsExcluded(emp.FullName ?? "")) return null;

        var cities = await db.AgentReachableCities
            .Where(c => c.EmployeeId == emp.EmployeeId)
            .Select(c => c.City).Distinct().ToListAsync();

        var allWics = await db.WicLocations.ToDictionaryAsync(w => w.LocationCode, StringComparer.OrdinalIgnoreCase);

        var empName = emp.FullName ?? "";
        var rawRoles = await db.WicAgentAssignments
            .Where(a => a.IsActive && a.EmployeeName == empName)
            .ToListAsync();

        var roles = rawRoles
            .Select(a => new WicRoleDto(
                a.LocationCode,
                allWics.TryGetValue(a.LocationCode, out var w) ? w.DisplayName : a.LocationCode,
                a.AssignmentType))
            .ToList();

        return new AgentCoverageDto(
            emp.EmployeeId, emp.PrimaryKid, emp.SecondaryKid, emp.FullName,
            emp.InfosysEmail, emp.EonEmail, emp.HasCar, emp.GroupRegion,
            [.. cities.Order()], [.. roles]);
    }

    public async Task<bool> PatchAgentAsync(string kid, PatchAgentDto dto)
    {
        var emp = await db.Employees
            .FirstOrDefaultAsync(e => e.PrimaryKid == kid || e.EmployeeId == kid);
        if (emp == null) return false;

        if (dto.HasCar.HasValue)         emp.HasCar      = dto.HasCar;
        if (dto.GroupRegion != null)      emp.GroupRegion = dto.GroupRegion;
        await db.SaveChangesAsync();
        return true;
    }

    // ── WICs ─────────────────────────────────────────────────────────────────

    public async Task<List<WicListItemDto>> GetWicsAsync(string? search)
    {
        var wics = await db.WicLocations.Where(w => w.IsActive).ToListAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            wics = wics
                .Where(w => w.DisplayName.ToLowerInvariant().Contains(s)
                         || (w.City ?? "").ToLowerInvariant().Contains(s)
                         || (w.Bundesland ?? "").ToLowerInvariant().Contains(s))
                .ToList();
        }

        var assignments = await db.WicAgentAssignments.Where(a => a.IsActive).ToListAsync();

        return wics
            .OrderBy(w => w.DisplayName)
            .Select(w => new WicListItemDto(
                w.LocationCode, w.DisplayName, w.City, w.Bundesland, w.OpeningDay,
                assignments.Count(a => a.LocationCode == w.LocationCode && a.AssignmentType == "MAIN"),
                assignments.Count(a => a.LocationCode == w.LocationCode && a.AssignmentType == "BACKUP")))
            .ToList();
    }

    public async Task<WicCoverageDto?> GetWicByCodeAsync(string locationCode)
    {
        var wic = await db.WicLocations.FirstOrDefaultAsync(w => w.LocationCode == locationCode);
        if (wic == null) return null;

        return await BuildWicCoverageDto(wic);
    }

    public async Task<List<AgentTierDto>> GetReachableAgentsAsync(string locationCode)
    {
        var wic = await db.WicLocations.FirstOrDefaultAsync(w => w.LocationCode == locationCode);
        if (wic == null) return [];

        var assignments = await db.WicAgentAssignments
            .Where(a => a.LocationCode == locationCode && a.IsActive)
            .ToListAsync();

        var alreadyAssigned = assignments
            .Select(a => a.EmployeeName.Trim().ToLowerInvariant())
            .ToHashSet();

        var baseCity = BaseCityOf(wic.DisplayName);
        var reachable = string.IsNullOrWhiteSpace(baseCity)
            ? []
            : await db.AgentReachableCities
                .Where(c => c.City == baseCity)
                .ToListAsync();

        var empIds = reachable
            .Where(c => c.EmployeeId != null && !alreadyAssigned.Contains(c.EmployeeName.ToLowerInvariant()))
            .Select(c => c.EmployeeId!)
            .Distinct()
            .ToList();

        var employees = await db.Employees
            .Where(e => empIds.Contains(e.EmployeeId))
            .ToListAsync();

        var byName = employees.ToLookup(e => e.FullName?.Trim().ToLowerInvariant() ?? "");

        return reachable
            .Where(c => c.EmployeeId != null && !alreadyAssigned.Contains(c.EmployeeName.ToLowerInvariant()))
            .Select(c => ToTierDto(c.EmployeeName, byName))
            .DistinctBy(d => d.Name.ToLowerInvariant())
            .Where(d => !IsExcluded(d.Name))
            .OrderBy(d => d.Name)
            .ToList();
    }

    public async Task<bool> PinBackupBAsync(string locationCode, PinBackupBDto dto)
    {
        var wic = await db.WicLocations.FirstOrDefaultAsync(w => w.LocationCode == locationCode);
        if (wic == null) return false;

        var name = dto.EmployeeName.Trim();
        if (string.IsNullOrWhiteSpace(name) || IsExcluded(name)) return false;

        bool exists = await db.WicAgentAssignments.AnyAsync(a =>
            a.LocationCode == locationCode &&
            string.Equals(a.EmployeeName, name, StringComparison.OrdinalIgnoreCase) &&
            a.AssignmentType == "BACKUP");

        if (!exists)
        {
            db.WicAgentAssignments.Add(new WicAgentAssignment
            {
                LocationCode   = locationCode,
                EmployeeName   = name,
                AssignmentType = "BACKUP",
                IsActive       = true,
                Notes          = "Pinned from Backup B",
            });
            await db.SaveChangesAsync();
        }
        return true;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<WicCoverageDto> BuildWicCoverageDto(WicLocation wic)
    {
        var assignments = await db.WicAgentAssignments
            .Where(a => a.LocationCode == wic.LocationCode && a.IsActive)
            .ToListAsync();

        var mainNames    = assignments.Where(a => a.AssignmentType == "MAIN")     .Select(a => a.EmployeeName).ToList();
        var backupANames = assignments.Where(a => a.AssignmentType == "BACKUP")   .Select(a => a.EmployeeName).ToList();
        var backupCNames = assignments.Where(a => a.AssignmentType == "REGIONAL") .Select(a => a.EmployeeName).ToList();

        var alreadyAssigned = mainNames.Concat(backupANames)
            .Select(n => n.Trim().ToLowerInvariant())
            .ToHashSet();

        var baseCity = BaseCityOf(wic.DisplayName);
        var reachableRows = string.IsNullOrWhiteSpace(baseCity)
            ? []
            : await db.AgentReachableCities
                .Where(c => c.City == baseCity)
                .ToListAsync();

        var backupBEmpIds = reachableRows
            .Where(c => c.EmployeeId != null && !alreadyAssigned.Contains(c.EmployeeName.Trim().ToLowerInvariant()))
            .Select(c => c.EmployeeId!)
            .Distinct()
            .ToList();

        var allRelevantNames = mainNames.Concat(backupANames).Concat(backupCNames)
            .Concat(reachableRows.Select(c => c.EmployeeName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var empsByName = (await db.Employees
            .Where(e => backupBEmpIds.Contains(e.EmployeeId)
                     || (e.FullName != null && allRelevantNames.Contains(e.FullName)))
            .ToListAsync())
            .ToLookup(e => e.FullName?.Trim().ToLowerInvariant() ?? "");

        AgentTierDto ToDto(string name) => ToTierDto(name, empsByName);

        var backupB = reachableRows
            .Where(c => !alreadyAssigned.Contains(c.EmployeeName.Trim().ToLowerInvariant()))
            .Select(c => ToDto(c.EmployeeName))
            .DistinctBy(d => d.Name.ToLowerInvariant())
            .Where(d => !IsExcluded(d.Name))
            .OrderBy(d => d.Name)
            .ToArray();

        var address = string.IsNullOrWhiteSpace(wic.FullAddress) ? null
            : string.Join(", ", new[] { wic.PostalCode, wic.City, wic.FullAddress }
                .Where(p => !string.IsNullOrWhiteSpace(p)));

        return new WicCoverageDto(
            wic.LocationCode, wic.DisplayName, wic.City, wic.Bundesland,
            wic.OpeningDay, wic.Comment, address,
            mainNames   .Where(n => !IsExcluded(n)).Select(ToDto).ToArray(),
            backupANames.Where(n => !IsExcluded(n)).Select(ToDto).ToArray(),
            backupB,
            backupCNames.Where(n => !IsExcluded(n)).Select(ToDto).ToArray()
        );
    }
}

// ─── Endpoint mapper ──────────────────────────────────────────────────────────

public static class WicCoverageEndpoints
{
    public static void MapWicCoverageEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/wic-coverage");

        grp.MapGet("/agents", async (WicCoverageService svc, string? search) =>
            Results.Ok(await svc.GetAgentsAsync(search)));

        grp.MapGet("/agents/{kid}", async (WicCoverageService svc, string kid) =>
        {
            var result = await svc.GetAgentByKidAsync(kid);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        grp.MapPatch("/agents/{kid}", async (WicCoverageService svc, string kid, PatchAgentDto dto) =>
        {
            var ok = await svc.PatchAgentAsync(kid, dto);
            return ok ? Results.Ok() : Results.NotFound();
        });

        grp.MapGet("/wics", async (WicCoverageService svc, string? search) =>
            Results.Ok(await svc.GetWicsAsync(search)));

        grp.MapGet("/wics/{locationCode}", async (WicCoverageService svc, string locationCode) =>
        {
            var result = await svc.GetWicByCodeAsync(locationCode);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        grp.MapGet("/wics/{locationCode}/reachable-agents", async (WicCoverageService svc, string locationCode) =>
            Results.Ok(await svc.GetReachableAgentsAsync(locationCode)));

        grp.MapPost("/wics/{locationCode}/backup-b", async (WicCoverageService svc, string locationCode, PinBackupBDto dto) =>
        {
            var ok = await svc.PinBackupBAsync(locationCode, dto);
            return ok ? Results.Ok() : Results.NotFound();
        });
    }
}
