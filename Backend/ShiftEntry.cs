using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

[Table("ShiftEntries")]
public class ShiftEntry
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(20)] public string EmployeeId { get; set; } = string.Empty;
    public DateOnly ShiftDate { get; set; }
    [MaxLength(200)] public string? RawValue { get; set; }
    [Required, MaxLength(20)] public string ShiftType { get; set; } = "EMPTY";
    [MaxLength(10)] public string? ShiftStart { get; set; }
    [MaxLength(10)] public string? ShiftEnd { get; set; }
    public bool IsWicDuty { get; set; } = false;
    [MaxLength(20)] public string? SourceSheet { get; set; }
    [MaxLength(20)] public string? AgentTask { get; set; }
    [MaxLength(50)] public string? LocationId { get; set; }
    [MaxLength(20)] public string? AssignmentStatus { get; set; }
}

public static class ShiftTypes
{
    public const string Working     = "WORKING";
    public const string WicDuty     = "WIC_DUTY";
    public const string AnnualLeave = "AL";
    public const string HalfAL      = "HALF_AL";
    public const string SickLeave   = "SL";
    public const string UnpaidLeave = "UL";
    public const string Off         = "OFF";
    public const string OffWeekend  = "OFF_WEEKEND";
    public const string PublicHol   = "PH";
    public const string LocalPH     = "LPH";
    public const string CompDay     = "CD";
    public const string OtherLeave  = "OL";
    public const string CompOff     = "CO";
    public const string Training    = "TRAINING";
    public const string Resigned    = "RESIGNED";
    public const string Empty       = "EMPTY";

    public static (string shiftType, string? start, string? end, bool isWic) Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (Empty, null, null, false);
        var v = raw.Trim();
        if (v.Contains("WIC")) { var (s, e) = ExtractTimes(v); return (WicDuty, s, e, true); }
        if (v == "AL")   return (AnnualLeave, null, null, false);
        if (v.Contains("HAL")) return (HalfAL, null, null, false);
        if (v == "SL")   return (SickLeave, null, null, false);
        if (v == "UL")   return (UnpaidLeave, null, null, false);
        if (v == "OFF")  return (Off, null, null, false);
        if (v == "OFFWE") return (OffWeekend, null, null, false);
        if (v == "PH")   return (PublicHol, null, null, false);
        if (v == "LPH")  return (LocalPH, null, null, false);
        if (v == "CD")   return (CompDay, null, null, false);
        if (v == "OL")   return (OtherLeave, null, null, false);
        if (v == "CO")   return (CompOff, null, null, false);
        if (v.Equals("Training", StringComparison.OrdinalIgnoreCase)) return (Training, null, null, false);
        if (v.Equals("Resigned", StringComparison.OrdinalIgnoreCase)) return (Resigned, null, null, false);
        if (v.Contains(":") && v.Contains(" - ")) { var (s, e) = ExtractTimes(v); return (Working, s, e, false); }
        return (Empty, null, null, false);
    }

    private static (string? start, string? end) ExtractTimes(string v)
    {
        var timeParts = v.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var times = timeParts.Where(p => p.Contains(':') && p.Length == 5).ToArray();
        if (times.Length >= 2) return (times[0], times[1]);
        if (times.Length == 1) return (times[0], null);
        return (null, null);
    }
}

