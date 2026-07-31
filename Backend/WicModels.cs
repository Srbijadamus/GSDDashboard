using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

[Table("WicAgentAssignments")]
public class WicAgentAssignment
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(50)]  public string LocationCode { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string EmployeeName { get; set; } = string.Empty;
    [Required, MaxLength(10)]  public string AssignmentType { get; set; } = "MAIN";
    public bool IsActive { get; set; } = true;
    [MaxLength(200)] public string? Notes { get; set; }
}

[Table("WicOpeningHours")]
public class WicOpeningHour
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(50)] public string LocationCode { get; set; } = string.Empty;
    public int DayOfWeek { get; set; }
    [MaxLength(10)] public string? OpenTime { get; set; }
    [MaxLength(10)] public string? CloseTime { get; set; }
    [MaxLength(10)] public string? OpenTime2 { get; set; }
    [MaxLength(10)] public string? CloseTime2 { get; set; }
    public bool IsClosed { get; set; } = false;
    [MaxLength(100)] public string? RawSchedule { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    [MaxLength(500)] public string? ChangeNote { get; set; }
    public int? MinRequired { get; set; }
}

[Table("WicPipeline")]
public class WicPipelineItem
{
    [Key] public int Id { get; set; }
    [MaxLength(50)]   public string? LocationCode { get; set; }
    public DateOnly PipelineDate { get; set; }
    public DateOnly? PipelineDateEnd { get; set; }
    [MaxLength(200)]  public string? Title { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    [MaxLength(200)]  public string? PrimaryAgent { get; set; }
    [MaxLength(200)]  public string? BackupAgent { get; set; }
    public int AdditionalAgentsNeeded { get; set; } = 0;
    [MaxLength(20)]   public string? HandledBy { get; set; }
    [MaxLength(200)]  public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(20)] public string Status { get; set; } = "PLANNED";
    [MaxLength(10)] public string? StartTime { get; set; }
    [MaxLength(10)] public string? EndTime { get; set; }
    public int AgentsRequired { get; set; } = 1;
}

[Table("LeaveQuotas")]
public class LeaveQuota
{
    [Key] public int Id { get; set; }
    public DateOnly QuotaDate { get; set; }
    public int MaxTotalLeave { get; set; }
    public int CurrentLeave { get; set; } = 0;
    [MaxLength(200)] public string? Notes { get; set; }
    [MaxLength(200)] public string? CreatedBy { get; set; }
}

[Table("VwicRotationSlots")]
public class VwicRotationSlot
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(20)] public string EmployeeId   { get; set; } = "";
    public DateOnly RotationDate { get; set; }
    [MaxLength(5)] public string SlotStart { get; set; } = "";
    [MaxLength(5)] public string SlotEnd   { get; set; } = "";
}

