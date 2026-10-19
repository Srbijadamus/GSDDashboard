using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.WicShifts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// The Overview gap list (GET /api/wic/coverage-gaps) must agree with WIC Attendance
// (/api/wic/cards and /api/wic/open). Reported 2026-10-19: RTM marked a WIC Helmstedt
// agent AL in the Shift Plan, WIC Attendance showed UNCOVERED, but the Overview V2 gap
// list showed "No gaps" for the same day. The other on-site agent (Merlin Voss) had a
// WORKING ShiftEntries row whose stale AgentTask still named the centre; the gap endpoint
// read that flipped-ShiftType encoding as WIC duty, while WIC Attendance reads the
// ShiftType strictly and treats a WORKING row as non-WIC (GSD) work.
//
// Pinned here: an on-site agent whose ShiftEntries row is WORKING (even with a centre
// name still in AgentTask) is NOT WIC cover for the gap list — the centre is a gap.
public class WicGapMatchesAttendanceTests
{
    private const string Code = "DE~99999~Gapheim~Lueckenweg 1";
    private const string Name = "Gapheim";

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void SeedCentre(GSDContext db, DateOnly date)
    {
        db.WicLocations.Add(new WicLocation
        {
            Id = 1, LocationCode = Code, DisplayName = Name, City = "Gapheim",
            MinAgentsRequired = 1, IsActive = true,
        });
        db.WicOpeningHours.Add(new WicOpeningHour
        {
            LocationCode = Code, DayOfWeek = (int)date.DayOfWeek, OpenTime = "08:00", CloseTime = "17:00",
        });
        db.WicAgentAssignments.Add(new WicAgentAssignment
        {
            LocationCode = Code, EmployeeName = "Gap Main", AssignmentType = "MAIN", IsActive = true,
        });
        db.Employees.Add(new Employee { EmployeeId = "G1", FullName = "Gap Main", IsActive = true });
    }

    [Fact]
    public async Task Gap_OnSiteAgentWhoseShiftIsWorkingWithStaleCentreName_IsNotCover()
    {
        // The reported shape: the MAIN agent is on site, but his ShiftEntries row was
        // manually set to WORKING and still carries the centre name in AgentTask. WIC
        // Attendance reads that as non-WIC work, so the centre is a gap — the gap list
        // must read it the same way.
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        foreach (var d in new[] { today, today.AddDays(7) })
        {
            db.WicShiftEntries.Add(new WicShiftEntry
            {
                EmployeeId = "G1", ShiftDate = d, SupportLocation = Name, IsOnSite = true, Task = "WIC",
            });
            db.ShiftEntries.Add(new ShiftEntry
            {
                EmployeeId = "G1", ShiftDate = d, ShiftType = ShiftTypes.Working,
                AgentTask = Name, ShiftStart = "08:00", ShiftEnd = "17:00",
                ManualChangeBy = "S60028",
            });
        }
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(14);

        Assert.Equal(2, result.Gaps.Count);           // today and today+7, the two open weekdays
        Assert.All(result.Gaps, g =>
        {
            Assert.Equal(0, g.Present);
            Assert.Equal(1, g.Required);
        });
    }

    [Fact]
    public async Task Gap_OnSiteAgentOnWicDuty_IsCover_NoGap()
    {
        // The mirror case that must not regress: a proper WIC_DUTY on-site agent still
        // counts as cover, so the centre is not a gap.
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var db = NewDb();
        SeedCentre(db, today);
        foreach (var d in new[] { today, today.AddDays(7) })
        {
            db.WicShiftEntries.Add(new WicShiftEntry
            {
                EmployeeId = "G1", ShiftDate = d, SupportLocation = Name, IsOnSite = true, Task = "WIC",
            });
            db.ShiftEntries.Add(new ShiftEntry
            {
                EmployeeId = "G1", ShiftDate = d, ShiftType = ShiftTypes.WicDuty,
                AgentTask = Name, ShiftStart = "08:00", ShiftEnd = "17:00", IsWicDuty = true,
            });
        }
        await db.SaveChangesAsync();

        var result = await new WicCoverageGapService(db).GetGapsAsync(14);

        Assert.Empty(result.Gaps);
    }
}
