using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace GSDDashboard.API.Data.Models;

[Table("PublicHolidays")]
public class PublicHoliday
{
    [Key] public int Id { get; set; }
    public DateOnly HolidayDate { get; set; }
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)]  public string? Bundesland { get; set; }
    [MaxLength(5)]   public string? Country { get; set; }   // null/empty = German (DE); "NL" = Dutch
    public bool IsNational { get; set; } = false;
}
