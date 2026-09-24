using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

// One roster generation batch. ShiftEntries rows written by a batch carry
// SourceModule = "Roster" and SourceId = RosterBatches.Id, so the whole batch
// can be found and deleted again (DELETE /api/roster/batches/{id}).
// The batch row itself is soft-deleted (DeletedAt) so the audit trail survives.
[Table("RosterBatches")]
public class RosterBatch
{
    [Key] public int Id { get; set; }

    [Required, MaxLength(20)]  public string  EmployeeId  { get; set; } = string.Empty;
    [MaxLength(200)]           public string? FullName    { get; set; }

    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo   { get; set; }

    [Required, MaxLength(10)]  public string  ShiftStart  { get; set; } = "08:00";
    [Required, MaxLength(10)]  public string  ShiftEnd    { get; set; } = "17:00";

    // Comma-separated .NET DayOfWeek numbers actually used (e.g. "1,2,3,4,5").
    [Required, MaxLength(30)]  public string  WorkingDays { get; set; } = "1,2,3,4,5";

    [MaxLength(20)]            public string? AgentTask   { get; set; }

    // Set for WIC rosters: the WicLocations.LocationCode whose opening days decided
    // which dates became WIC_DUTY (the rest became BO). Null for plain rosters.
    [MaxLength(50)]            public string? LocationCode { get; set; }

    public int RowCount        { get; set; }
    public int SkippedExisting { get; set; }
    public int SkippedHolidays { get; set; }

    // Overwrite mode: how many existing rows the batch replaced. Their original
    // content (plus WicShiftEntries rows for replaced WIC_DUTY days) is kept in
    // ReplacedRowsJson so DeleteBatchAsync can restore the pre-batch state.
    public int     ReplacedExisting  { get; set; }
    public string? ReplacedRowsJson  { get; set; }

    [MaxLength(20)]            public string? CreatedByKid  { get; set; }
    [MaxLength(200)]           public string? CreatedByName { get; set; }

    public DateTime  CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}
