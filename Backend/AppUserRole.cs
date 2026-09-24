using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

// Login + role table. This is the ONLY place application roles live —
// roles are intentionally NOT columns on Employees (see documentation/auth_rbac.md).
//
// Non-employees (developer, RTMs, team leads) exist here with EmployeeId = NULL.
// Regular agents usually have NO row here at all: they resolve directly from the
// Employees table at login time (AuthService rules b/c). A row with Kid = ''
// (placeholder) must never authenticate — AuthService enforces that.
[Table("AppUserRoles")]
public class AppUserRole
{
    [Key] public int Id { get; set; }

    // Login identifier (KID-style code, e.g. S69307). '' = placeholder, cannot log in.
    // Unique per non-empty value (filtered unique index UX_AppUserRoles_Kid).
    [Required, MaxLength(20)] public string Kid { get; set; } = string.Empty;

    // One of AppRoles.* — DEV, RTM, TEAM_LEAD, AGENT.
    [Required, MaxLength(20)] public string Role { get; set; } = string.Empty;

    // NULL for non-employees. Mandatory for Role = AGENT (enforced at startup).
    [MaxLength(20)] public string? EmployeeId { get; set; }

    [Required, MaxLength(200)] public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public static class AppRoles
{
    public const string Agent    = "AGENT";      // own data only, read-only
    public const string TeamLead = "TEAM_LEAD";  // full read/write
    public const string Rtm      = "RTM";        // full read/write (same rights as TEAM_LEAD)
    public const string Dev      = "DEV";        // full access, bypasses all policies

    public static readonly string[] All = { Agent, TeamLead, Rtm, Dev };

    public static bool IsValid(string? role) =>
        role != null && All.Contains(role, StringComparer.OrdinalIgnoreCase);
}
