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
    public bool IsNational { get; set; } = false;
}
