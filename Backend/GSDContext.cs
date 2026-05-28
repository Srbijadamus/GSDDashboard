using Microsoft.EntityFrameworkCore;
using GSDDashboard.API.Data.Models;

namespace GSDDashboard.API.Data;

public class GSDContext : DbContext
{
    public GSDContext(DbContextOptions<GSDContext> options) : base(options) { }

    public DbSet<Employee>           Employees           { get; set; }
    public DbSet<ShiftEntry>         ShiftEntries        { get; set; }
    public DbSet<WicShiftEntry>      WicShiftEntries     { get; set; }
    public DbSet<WicLocation>        WicLocations        { get; set; }
    public DbSet<DailyAttendance>    DailyAttendances    { get; set; }
    public DbSet<SickLeave>          SickLeaves          { get; set; }
    public DbSet<Vacation>           Vacations           { get; set; }
    public DbSet<ALBalance>          ALBalances          { get; set; }
    public DbSet<WicAgentAssignment> WicAgentAssignments { get; set; }
    public DbSet<WicOpeningHour>     WicOpeningHours     { get; set; }
    public DbSet<WicPipelineItem>    WicPipeline         { get; set; }
    public DbSet<LeaveQuota>         LeaveQuotas         { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(e => {
            e.HasIndex(x => x.EmployeeId).IsUnique();
        });
        modelBuilder.Entity<ShiftEntry>(e => {
            e.HasIndex(x => x.ShiftDate).HasDatabaseName("IX_Shift_Date");
            e.HasIndex(x => x.EmployeeId).HasDatabaseName("IX_Shift_Emp");
            e.HasIndex(x => x.ShiftType).HasDatabaseName("IX_Shift_Type");
            e.HasIndex(x => new { x.EmployeeId, x.ShiftDate, x.SourceSheet })
             .IsUnique().HasDatabaseName("UQ_ShiftEntries_EmpDateSheet");
        });
        modelBuilder.Entity<WicShiftEntry>(e => {
            e.HasIndex(x => x.ShiftDate).HasDatabaseName("IX_Wic_Date");
            e.HasIndex(x => x.EmployeeId).HasDatabaseName("IX_Wic_Emp");
            e.HasIndex(x => x.SupportLocation).HasDatabaseName("IX_Wic_Location");
            e.HasIndex(x => new { x.EmployeeId, x.ShiftDate })
             .IsUnique().HasDatabaseName("UQ_WicShift_EmpDate");
        });
        modelBuilder.Entity<WicLocation>(e => {
            e.HasIndex(x => x.LocationCode).IsUnique();
        });
        modelBuilder.Entity<DailyAttendance>(e => {
            e.HasIndex(x => x.AttendanceDate).HasDatabaseName("IX_Att_Date");
            e.HasIndex(x => x.LocationName).HasDatabaseName("IX_Att_Location");
            e.HasIndex(x => x.Country).HasDatabaseName("IX_Att_Country");
        });
        modelBuilder.Entity<SickLeave>(e => {
            e.HasIndex(x => x.EmployeeId).HasDatabaseName("IX_SL_Emp");
            e.HasIndex(x => new { x.FirstDay, x.LastDay }).HasDatabaseName("IX_SL_Dates");
            e.HasIndex(x => x.TeamLeadName).HasDatabaseName("IX_SL_TL");
            e.HasIndex(x => new { x.EmployeeId, x.FirstDay })
             .IsUnique().HasDatabaseName("UQ_SickLeaves_EmpFirstDay");
        });
        modelBuilder.Entity<Vacation>(e => {
            e.HasIndex(x => x.EmployeeId).HasDatabaseName("IX_Vac_Emp");
            e.HasIndex(x => new { x.FirstDay, x.LastDay }).HasDatabaseName("IX_Vac_Dates");
        });
        modelBuilder.Entity<ALBalance>(e => {
            e.HasIndex(x => x.EmployeeId).IsUnique();
        });
        modelBuilder.Entity<WicAgentAssignment>(e => {
            e.HasIndex(x => x.LocationCode).HasDatabaseName("IX_WicAssign_Location");
            e.HasIndex(x => x.EmployeeName).HasDatabaseName("IX_WicAssign_Employee");
        });
        modelBuilder.Entity<WicOpeningHour>(e => {
            e.HasIndex(x => x.LocationCode).HasDatabaseName("IX_WicHours_Location");
            e.HasIndex(x => x.DayOfWeek).HasDatabaseName("IX_WicHours_Day");
        });
        modelBuilder.Entity<WicPipelineItem>(e => {
            e.HasIndex(x => x.PipelineDate).HasDatabaseName("IX_Pipeline_Date");
            e.HasIndex(x => x.LocationCode).HasDatabaseName("IX_Pipeline_Location");
        });
        modelBuilder.Entity<LeaveQuota>(e => {
            e.HasIndex(x => x.QuotaDate).HasDatabaseName("IX_Quota_Date");
        });
        base.OnModelCreating(modelBuilder);
    }
}
