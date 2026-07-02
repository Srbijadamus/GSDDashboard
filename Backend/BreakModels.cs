using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

public enum BreakStatus { SCHEDULED, ON_BREAK, DONE, CANCELLED }

[Table("BreakSlots")]
public class BreakSlot
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(20)] public string EmployeeId { get; set; } = "";
    public DateOnly BreakDate { get; set; }
    // Stored as strings ("HH:mm") for consistency with ShiftEntry.ShiftStart/ShiftEnd
    [MaxLength(10)] public string BreakStart { get; set; } = "";
    [MaxLength(10)] public string BreakEnd   { get; set; } = "";
    [MaxLength(10)] public string? ActualStart { get; set; }
    [MaxLength(10)] public string? ActualEnd   { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public BreakStatus Status { get; set; } = BreakStatus.SCHEDULED;
    [MaxLength(20)] public string? AgentRole { get; set; }  // "VWIC" | "Voice"
}
