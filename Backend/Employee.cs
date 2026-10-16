using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

[Table("Employees")]
public class Employee
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(20)] public string EmployeeId { get; set; } = string.Empty;
    [MaxLength(100)] public string? FirstName { get; set; }
    [MaxLength(100)] public string? LastName { get; set; }
    [MaxLength(200)] public string? FullName { get; set; }
    [MaxLength(50)]  public string? Engagement { get; set; }
    [MaxLength(50)]  public string? PrimaryRole { get; set; }
    [MaxLength(50)]  public string? SecondaryRole { get; set; }
    [MaxLength(200)] public string? TeamLeadName { get; set; }
    [MaxLength(50)]  public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsTrainee { get; set; } = false;
    [MaxLength(50)]  public string? PlannedRole { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateOnly? Birthday { get; set; }
    [MaxLength(20)]  public string? SourceSheet { get; set; }
    [MaxLength(50)]  public string? Bundesland { get; set; }
    [MaxLength(20)]  public string? PrimaryKid   { get; set; }
    [MaxLength(20)]  public string? SecondaryKid  { get; set; }
    [MaxLength(200)] public string? InfosysEmail  { get; set; }
    [MaxLength(200)] public string? EonEmail      { get; set; }
    public bool?     HasCar      { get; set; }
    [MaxLength(100)] public string? GroupRegion   { get; set; }
    [MaxLength(20)]  public string? ShiftPattern  { get; set; }
    public bool NppQualified { get; set; } = false;

    /// <summary>
    /// ON DEMAND / NOT ROSTERED BY US (owner decision, 2026-10-16). An agent who works on
    /// another project and is engaged only when needed. Such a person has no roster of his
    /// own, so the Overview "Roster gaps" banner (<c>GET /api/roster/missing</c>) must not
    /// list him — a permanent false alarm that hides the real gaps.
    ///
    /// The flag ONLY silences that banner. The agent stays ACTIVE, stays assignable by hand
    /// (Shift Plan, WIC assignment, Bulk RTM) and stays visible to the substitute/candidate
    /// suggestions: those are exactly the "engaged only when needed" paths.
    ///
    /// Additive and defaulted false: no existing row is changed by the migration; the AL
    /// Tracker and ShiftEntries are not touched.
    /// </summary>
    public bool IsOnDemand { get; set; } = false;
}

// The agent's default daily shift-time slot (distinct from ShiftEntry.ShiftType,
// which is the per-day work/absence status).
public static class ShiftPatterns
{
    public const string Early     = "EARLY";
    public const string Morning   = "MORNING";
    public const string Afternoon = "AFTERNOON";
    public const string Night     = "NIGHT";
    public const string Backup    = "BACKUP";

    public static readonly string[] All = { Early, Morning, Afternoon, Night, Backup };

    /// <summary>
    /// Default shift times for a pattern, or (null, null) when the pattern does
    /// not imply an hour. The start hours are the ones the Employees page spells
    /// out next to each pattern ("Early (06/07)", "Morning (08)",
    /// "Afternoon (13)"); EARLY resolves to 07:00, the far more common of its two
    /// hours in ShiftEntries. The 9h span is the standard working span in the
    /// data and nets the 8h ShiftValidationService already assumes once it
    /// subtracts the 1h break for any shift over 6h. NIGHT uses the 22:00-07:00
    /// example BulkRtm states for night shifts. BACKUP implies no hour, so a
    /// caller falls back to its own default.
    /// </summary>
    public static (string? Start, string? End) DefaultTimes(string? pattern) => pattern?.ToUpperInvariant() switch
    {
        Early     => ("07:00", "16:00"),
        Morning   => ("08:00", "17:00"),
        Afternoon => ("13:00", "22:00"),
        Night     => ("22:00", "07:00"),
        _         => (null, null),
    };
}

