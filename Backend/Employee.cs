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
}

