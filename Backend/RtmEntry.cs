using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

[Table("RtmEntries")]
public class RtmEntry
{
    [Key] public int Id { get; set; }
    public DateOnly EntryDate { get; set; }
    [Required, MaxLength(20)]  public string  EmployeeId  { get; set; } = string.Empty;
    [MaxLength(200)]           public string? FullName    { get; set; }
    [Required, MaxLength(10)]  public string  ShiftStart  { get; set; } = "08:00";
    [Required, MaxLength(10)]  public string  ShiftEnd    { get; set; } = "17:00";
    [MaxLength(500)]           public string? Tag         { get; set; }
    [MaxLength(500)]           public string? SourceLine  { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
