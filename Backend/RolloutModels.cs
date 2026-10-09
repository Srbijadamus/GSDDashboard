using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

/// <summary>
/// Marks rows the ROLLOUT feature created inside an EXISTING table (ShiftEntries), so the
/// rollout can later remove exactly what it made and nothing else. There is no
/// "ManualSource" column anywhere in the schema (see ShiftEntry.cs) — SourceModule is the
/// established provenance column and is used here for that purpose.
/// </summary>
public static class RolloutSource
{
    public const string Module = "ROLLOUT";
    public const string EntryNote =
        "Rollout day (Rollout page); automatic writers must preserve this row.";
}

/// <summary>
/// A ROLLOUT DAY: one WIC centre, opened on one date especially for new workers.
///
/// This is a MANUAL, PRIMARY entry by RTM — exactly like the other hand-entered rows.
/// It is stored here as the single source of truth and is never rewritten, moved or
/// deleted by any automatic writer (roster generator, imports, substitutions, startup
/// reconciliation). Two derived things are created from it and are removed with it:
///   * one WicOpeningHours EXCEPTION row (ExceptionDate = this date) so every surface
///     that already resolves hours through WicHoursResolver uses the rollout hours
///     instead of the standard pattern for that centre and date; and
///   * one WicShiftEntries row plus, when the worker has no row for that day yet, one
///     ShiftEntries WIC_DUTY row per selected worker, so the rollout workers appear as
///     assigned to that centre at that time in WIC Shifts, WIC Attendance, coverage,
///     forecast and the conflict detector.
///
/// Additive table (2026-10-08). No existing table or column was changed to add it.
/// </summary>
[Table("WicRolloutDays")]
public class WicRolloutDay
{
    [Key] public int Id { get; set; }

    [Required, MaxLength(50)]  public string LocationCode { get; set; } = string.Empty;
    [MaxLength(200)]           public string? LocationName { get; set; }

    public DateOnly RolloutDate { get; set; }

    /// <summary>Rollout working hours, HH:MM. These replace the centre's standard hours
    /// for this centre and this date only.</summary>
    [Required, MaxLength(5)] public string StartTime { get; set; } = string.Empty;
    [Required, MaxLength(5)] public string EndTime   { get; set; } = string.Empty;

    [MaxLength(1000)] public string? Note { get; set; }

    /// <summary>Always true today: an RTM entered this day by hand. Kept explicit so the
    /// marker survives a future automatic writer that might otherwise claim the row.</summary>
    public bool IsManual { get; set; } = true;

    /// <summary>Who entered or last changed this rollout day (KID or "UI-MANUAL"), and
    /// when. Same actor contract as ResolveManualActor / RecordManualShiftEdit.</summary>
    [MaxLength(100)] public string? ManualChangeBy { get; set; }
    public DateTime? ManualChangeAt { get; set; }
    [MaxLength(500)] public string? ManualChangeNote { get; set; }

    /// <summary>The WicOpeningHours exception row this rollout projected, so an edit or a
    /// delete touches only that row and never a pre-existing one.</summary>
    public int? HoursRowId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<WicRolloutWorker> Workers { get; set; } = new();
}

/// <summary>One worker on one rollout day. See <see cref="WicRolloutDay"/>.</summary>
[Table("WicRolloutWorkers")]
public class WicRolloutWorker
{
    [Key] public int Id { get; set; }

    public int RolloutDayId { get; set; }

    [Required, MaxLength(20)] public string EmployeeId { get; set; } = string.Empty;
    [MaxLength(200)]          public string? EmployeeName { get; set; }

    /// <summary>True when this worker was offered by the Pipeline page for this centre and
    /// date. False means RTM deliberately took someone who is NOT in Pipeline — the UI
    /// requires that decision to be explicit and labels it.</summary>
    public bool FromPipeline { get; set; }

    /// <summary>Why an out-of-Pipeline worker was chosen (optional, RTM text).</summary>
    [MaxLength(500)] public string? PipelineNote { get; set; }

    /// <summary>Ids of the rows this entry created in the existing tables (null when the
    /// worker already had a row for that day, which is never overwritten).</summary>
    public int? ShiftEntryId    { get; set; }
    public int? WicShiftEntryId { get; set; }

    public WicRolloutDay? RolloutDay { get; set; }
}
