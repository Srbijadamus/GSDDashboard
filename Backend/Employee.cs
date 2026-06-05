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
}

