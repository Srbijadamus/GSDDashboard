using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace GSDDashboard.API.Data.Models;

[Table("TrainingTopics")]
public class TrainingTopic
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public int DurationHours { get; set; } = 2;
    public int MinGroupSize  { get; set; } = 3;
    public int MaxGroupSize  { get; set; } = 15;
    public bool IsMandatory  { get; set; } = false;
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("TrainingSchedule")]
public class TrainingSession
{
    [Key] public int Id { get; set; }
    public int TopicId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    [MaxLength(10)] public string StartTime { get; set; } = string.Empty;
    [MaxLength(10)] public string EndTime { get; set; } = string.Empty;
    public string? AgentIds { get; set; }
    [MaxLength(200)] public string? SuggestedBy { get; set; }
    [MaxLength(200)] public string? ConfirmedBy { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "SUGGESTED";
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
