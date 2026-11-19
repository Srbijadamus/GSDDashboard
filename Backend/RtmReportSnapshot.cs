using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

/// <summary>
/// Snapshot of an RTM report the moment it was marked as sent ("Mark as sent").
///
/// A WEEKLY snapshot is the 7-day WIC forecast; a DAILY snapshot is the current plan for
/// the remaining window of the last sent report. "Daily corrections" diffs the current plan
/// against the LATEST snapshot (weekly or daily), so every snapshot must store the plan it
/// displayed, not just a timestamp. CreatedBy is the session KID that marked it sent.
///
/// Additive and idempotent: the table is created by the startup DDL in Program.cs (same
/// IF NOT EXISTS sys.tables guard as BoEntries / RtmEntries). No existing table is touched.
/// </summary>
[Table("RtmReportSnapshots")]
public class RtmReportSnapshot
{
    [Key] public int Id { get; set; }

    /// <summary>"WEEKLY" or "DAILY".</summary>
    [Required, MaxLength(20)] public string ReportType { get; set; } = string.Empty;

    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Session KID of the RTM/DEV who marked the report as sent.</summary>
    [MaxLength(100)] public string? CreatedBy { get; set; }

    /// <summary>The displayed report plan, serialised (RtmPlanResponse JSON).</summary>
    public string? PayloadJson { get; set; }
}
