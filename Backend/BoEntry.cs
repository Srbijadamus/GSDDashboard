using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

[Table("BoEntries")]
public class BoEntry
{
    [Key] public int Id { get; set; }
    public DateOnly EntryDate { get; set; }
    [Required, MaxLength(200)] public string EmployeeName { get; set; } = string.Empty;
    [Required, MaxLength(10)]  public string ShiftStart   { get; set; } = "08:00";
    [Required, MaxLength(10)]  public string ShiftEnd     { get; set; } = "17:00";
    [MaxLength(500)]           public string? Note        { get; set; }
    public int SortOrder { get; set; }
}
