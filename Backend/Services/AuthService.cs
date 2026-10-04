using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GSDDashboard.API.Services;

public record AuthResult(string Role, string? EmployeeId, string DisplayName, string Kid);

/// <summary>
/// KID-only login resolution. Order (decided in odgovor3.md, do not reorder):
///   a) AppUserRoles: IsActive = 1 AND Kid non-empty AND Kid exact match → that role
///      (EmployeeId may be NULL for non-employees; an AGENT row MUST have an EmployeeId).
///   b) Employees: IsActive = 1 AND (PrimaryKid OR SecondaryKid exact match) → AGENT.
///   c) Employees: IsActive = 1 AND EmployeeId exact match → AGENT.
///      (Precedent: WicCoverageService.GetAgentByKidAsync already accepts either.)
///   d) otherwise → null (caller answers 401 with the neutral "Unknown KID").
/// Any input matching MORE THAN ONE active row at any step → null + warning log.
/// Never guess.
/// </summary>
public class AuthService
{
    private readonly GSDContext _db;
    private readonly ILogger<AuthService> _log;

    public AuthService(GSDContext db, ILogger<AuthService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<AuthResult?> ResolveLoginAsync(string? rawInput)
    {
        var kid = (rawInput ?? string.Empty).Trim();
        if (kid.Length == 0 || kid.Length > 20) return null;

        // (a) AppUserRoles — the login table. Empty-Kid placeholder rows can never match.
        var roleRows = await _db.AppUserRoles
            .Where(r => r.IsActive && r.Kid != "" && r.Kid == kid)
            .ToListAsync();
        if (roleRows.Count > 1)
        {
            _log.LogWarning("Login denied: KID matches {Count} active AppUserRoles rows", roleRows.Count);
            return null;
        }
        if (roleRows.Count == 1)
        {
            var row = roleRows[0];
            if (!AppRoles.IsValid(row.Role))
            {
                _log.LogWarning("Login denied: AppUserRoles row {Id} has unknown role {Role}", row.Id, row.Role);
                return null;
            }
            if (row.Role == AppRoles.Agent && string.IsNullOrWhiteSpace(row.EmployeeId))
            {
                // Should be impossible — Program.cs refuses to start in this state.
                _log.LogError("Login denied: AGENT AppUserRoles row {Id} has no EmployeeId", row.Id);
                return null;
            }
            return new AuthResult(row.Role, row.EmployeeId, row.DisplayName, row.Kid);
        }

        // (b) Employees by PrimaryKid / SecondaryKid → AGENT
        var byKid = await _db.Employees
            .Where(e => e.IsActive && (e.PrimaryKid == kid || e.SecondaryKid == kid))
            .Select(e => new { e.EmployeeId, e.FullName })
            .ToListAsync();
        if (byKid.Count > 1)
        {
            _log.LogWarning("Login denied: KID matches {Count} active Employees rows (PrimaryKid/SecondaryKid)", byKid.Count);
            return null;
        }
        if (byKid.Count == 1)
            return new AuthResult(AppRoles.Agent, byKid[0].EmployeeId, byKid[0].FullName ?? byKid[0].EmployeeId, kid);

        // (c) Employees by EmployeeId → AGENT (63 active agents have no KID at all)
        var byId = await _db.Employees
            .Where(e => e.IsActive && e.EmployeeId == kid)
            .Select(e => new { e.EmployeeId, e.FullName })
            .ToListAsync();
        if (byId.Count > 1)
        {
            _log.LogWarning("Login denied: input matches {Count} active Employees rows (EmployeeId)", byId.Count);
            return null;
        }
        if (byId.Count == 1)
            return new AuthResult(AppRoles.Agent, byId[0].EmployeeId, byId[0].FullName ?? byId[0].EmployeeId, kid);

        // (d) unknown
        return null;
    }
}
