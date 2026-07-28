using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GSDDashboard.API.Data.Models;

[Table("WicShiftEntries")]
public class WicShiftEntry
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(20)] public string EmployeeId { get; set; } = string.Empty;
    public DateOnly ShiftDate { get; set; }
    [MaxLength(10)]  public string? DayOfWeek { get; set; }
    [MaxLength(200)] public string? SupportLocation { get; set; }
    [MaxLength(50)]  public string? WicOpeningHours { get; set; }
    [MaxLength(50)]  public string? WorkingShift { get; set; }
    public bool IsOnSite { get; set; } = false;
    public bool IsGSDDay { get; set; } = false;
    public bool IsOffDay { get; set; } = false;
    [MaxLength(20)]  public string? Task { get; set; } = "WIC";
}

[Table("WicLocations")]
public class WicLocation
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(50)]  public string LocationCode { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(500)] public string? FullAddress { get; set; }
    [MaxLength(20)]  public string? PostalCode { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(5)]   public string? Country { get; set; }
    [MaxLength(200)] public string? OpeningSchedule { get; set; }
    [MaxLength(50)]  public string? Coordinates { get; set; }     // "lat,lon" e.g. "48.3705,10.8978"
    public int?      MinAgentsRequired { get; set; }              // null = use default (1)
    [MaxLength(50)]  public string? Bundesland { get; set; }      // for regional holiday matching
    [MaxLength(50)]  public string? LocationCodeLegacy { get; set; } // old-style code used in WicAgentAssignments (e.g. DE_Dortmund)
    public bool IsActive { get; set; } = true;
    public bool IsNpp   { get; set; } = false;
    [MaxLength(200)]  public string? OpeningDay { get; set; }
    [MaxLength(1000)] public string? Comment    { get; set; }
}

[Table("AgentReachableCities")]
public class AgentReachableCity
{
    [Key] public int Id { get; set; }
    [MaxLength(20)]  public string? EmployeeId   { get; set; }
    [Required, MaxLength(200)] public string EmployeeName { get; set; } = "";
    [Required, MaxLength(200)] public string City         { get; set; } = "";
    [Required, MaxLength(20)]  public string Source       { get; set; } = "seed";
}

[Table("SickLeaves")]
public class SickLeave
{
    [Key] public int Id { get; set; }
    [MaxLength(20)]  public string? EmployeeId { get; set; }
    [MaxLength(100)] public string? FirstName { get; set; }
    [MaxLength(100)] public string? LastName { get; set; }
    [MaxLength(200)] public string? FullName { get => (FirstName + " " + LastName).Trim(); }
    [MaxLength(200)] public string? TeamLeadName { get; set; }
    public DateOnly FirstDay { get; set; }
    public DateOnly LastDay { get; set; }
    public int? DurationDays { get; set; }
    [MaxLength(10)]  public string? LeaveType { get; set; }
    [MaxLength(200)] public string? ChildName { get; set; }
    [MaxLength(500)] public string? Comments { get; set; }
    [MaxLength(50)]  public string? SourceSheet { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("Vacations")]
public class Vacation
{
    [Key] public int Id { get; set; }
    [MaxLength(20)]  public string? EmployeeId { get; set; }
    [MaxLength(100)] public string? LastName { get; set; }
    [MaxLength(100)] public string? FirstName { get; set; }
    public DateOnly FirstDay { get; set; }
    public DateOnly LastDay { get; set; }
    public int? WorkDaysNet { get; set; }
    [MaxLength(500)] public string? Comments { get; set; }
    [MaxLength(20)]  public string? ApprovedDenied { get; set; }
    [MaxLength(200)] public string? ApproverName { get; set; }
    public DateOnly? ApproverDate { get; set; }
    public DateOnly? MonthStart { get; set; }
    public int? SourceYear { get; set; }
    [MaxLength(20)]  public string? SourceSheet { get; set; }
    public bool IsOverhead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("ALBalance")]
public class ALBalance
{
    [Key] public int Id { get; set; }
    [MaxLength(20)]  public string? EmployeeId { get; set; }
    [MaxLength(200)] public string? EmployeeName { get; set; }
    public int EligibleDays { get; set; } = 28;
    public int PlannedTakenAL { get; set; } = 0;
    public int RemainingAL { get; set; } = 0;
    public int CountSL { get; set; } = 0;
    public int CountUL { get; set; } = 0;
    public int CountWorkingSundays { get; set; } = 0;
    public int CountFreeSundays { get; set; } = 0;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

[Table("DailyAttendance")]
public class DailyAttendance
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(200)] public string LocationName { get; set; } = string.Empty;
    [MaxLength(5)]   public string? Country { get; set; }
    public DateOnly AttendanceDate { get; set; }
    [MaxLength(100)] public string? RawValue { get; set; }
    [MaxLength(20)]  public string? AttendanceType { get; set; }
    [MaxLength(20)]  public string? AssignedEmployeeId { get; set; }
}

[Table("SubstitutionHistory")]
public class SubstitutionHistory
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(20)]  public string EmployeeId { get; set; } = "";
    [Required, MaxLength(100)] public string LocationCode { get; set; } = "";
    public DateOnly Date { get; set; }
    [MaxLength(20)] public string? SourceType { get; set; }  // BACKUP | SSP | WIC_DONOR | CALL_IN
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}




