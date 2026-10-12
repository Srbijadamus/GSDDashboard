using System.Security.Claims;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GSDDashboard.Tests;

// Roster creation (RTM): preview/generate/delete over ShiftEntries.
// Rules under test:
//   - weekends (Sat/Sun) always skipped, same as ShiftSyncService
//   - public holidays skipped like PublicHolidayService (national + own Bundesland)
//   - existing ShiftEntry on a date -> collision, never overwritten
//   - generated rows carry SourceModule="Roster" + SourceId=batch id
//   - role gate: RTM/TEAM_LEAD/DEV only, AGENT gets 403, anonymous 401
public class RosterServiceTests
{
    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // RosterService.GenerateAsync runs inside a transaction; the in-memory
            // store has none and warns. Same suppression the other roster test files
            // use (RosterMultiMainSameDayTests / RosterMultiMainPerDayTests).
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new GSDContext(options);
    }

    private static RosterService NewSvc(GSDContext db) =>
        new(db, new WicShiftService(db, new AvailabilityResolver(db)), new WicCoverageGapService(db));

    private static Employee SeedEmployee(GSDContext db, string id, string? bundesland = null)
    {
        var emp = new Employee { EmployeeId = id, FullName = "Agent " + id, IsActive = true, Bundesland = bundesland };
        db.Employees.Add(emp);
        return emp;
    }

    private static RosterRequest Req(string empId, string from, string to,
        string start = "08:00", string end = "16:30", int[]? days = null, string? task = null) =>
        new(empId, from, to, start, end, days, task);

    // 2026-09-21 is a Monday, 2026-09-27 a Sunday.

    [Fact]
    public async Task Preview_SkipsWeekends()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        await db.SaveChangesAsync();

        var p = await NewSvc(db).PreviewAsync(Req("E1", "2026-09-21", "2026-09-27"));

        Assert.Equal(5, p.RowCount);
        Assert.Equal(2, p.SkippedWeekends);
        Assert.Equal("2026-09-21", p.FirstDate);
        Assert.Equal("2026-09-25", p.LastDate);
        Assert.Empty(p.Collisions);
    }

    [Fact]
    public async Task Preview_SkipsNationalHoliday()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        db.PublicHolidays.Add(new PublicHoliday { HolidayDate = new DateOnly(2026, 9, 23), Name = "Testfeiertag", IsNational = true });
        await db.SaveChangesAsync();

        var p = await NewSvc(db).PreviewAsync(Req("E1", "2026-09-21", "2026-09-25"));

        Assert.Equal(4, p.RowCount);
        var h = Assert.Single(p.SkippedHolidays);
        Assert.Equal("2026-09-23", h.Date);
        Assert.Equal("Testfeiertag", h.Name);
    }

    [Fact]
    public async Task Preview_RegionalHoliday_OnlySkippedForThatBundesland()
    {
        using var db = NewDb();
        SeedEmployee(db, "NRW", "NRW");
        SeedEmployee(db, "BY", "Bayern");
        db.PublicHolidays.Add(new PublicHoliday { HolidayDate = new DateOnly(2026, 9, 23), Name = "Bayern-Tag", IsNational = false, Bundesland = "Bayern" });
        await db.SaveChangesAsync();

        var nrw = await NewSvc(db).PreviewAsync(Req("NRW", "2026-09-21", "2026-09-25"));
        var by  = await NewSvc(db).PreviewAsync(Req("BY",  "2026-09-21", "2026-09-25"));

        Assert.Equal(5, nrw.RowCount);           // Bayern-only holiday does not apply in NRW
        Assert.Equal(4, by.RowCount);            // ...but does in Bayern
        Assert.Single(by.SkippedHolidays);
    }

    [Fact]
    public async Task Preview_RespectsWorkingDays()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        await db.SaveChangesAsync();

        // Mon/Wed/Fri only
        var p = await NewSvc(db).PreviewAsync(Req("E1", "2026-09-21", "2026-09-27", days: new[] { 1, 3, 5 }));

        Assert.Equal(3, p.RowCount);
        Assert.Equal(new[] { "2026-09-21", "2026-09-23", "2026-09-25" }, p.Rows.Select(r => r.Date).ToArray());
    }

    [Fact]
    public async Task Preview_ExistingRow_IsCollision_NeverIncluded()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E1", ShiftDate = new DateOnly(2026, 9, 22), ShiftType = "AL", SourceModule = "Vacation" });
        await db.SaveChangesAsync();

        var p = await NewSvc(db).PreviewAsync(Req("E1", "2026-09-21", "2026-09-25"));

        Assert.Equal(4, p.RowCount);
        var c = Assert.Single(p.Collisions);
        Assert.Equal("2026-09-22", c.Date);
        Assert.Equal("AL", c.ShiftType);
        Assert.Equal("Vacation", c.SourceModule);
        Assert.DoesNotContain(p.Rows, r => r.Date == "2026-09-22");
    }

    // CO (comp off) appears in neither documented list: technical.md "Roster
    // generator" says overwrite mode replaces WORKING/BO/WIC_DUTY/empty rows,
    // and ProtectedAbsenceTypes (FUNCTIONS.md) names AL, HALF_AL, SL, UL, PH,
    // LPH, OL, OFF, OFF_WEEKEND, RESIGNED, CD — no CO. Pinning what the code
    // actually does, since the docs do not say.
    [Fact]
    public async Task Preview_CompOffRow_IsNotProtected_AndIsReplacedInOverwriteMode()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E1", ShiftDate = new DateOnly(2026, 9, 22), ShiftType = "CO", SourceModule = "PATTERN_FILL" });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E1", ShiftDate = new DateOnly(2026, 9, 23), ShiftType = "OFF", SourceModule = "PATTERN_FILL" });
        await db.SaveChangesAsync();

        var svc = NewSvc(db);

        // Overwrite off: both are skipped, but only OFF is reported as protected.
        var off = await svc.PreviewAsync(Req("E1", "2026-09-21", "2026-09-25"));
        Assert.Equal(3, off.RowCount);
        Assert.False(Assert.Single(off.Collisions, c => c.Date == "2026-09-22").Protected);
        Assert.True(Assert.Single(off.Collisions, c => c.Date == "2026-09-23").Protected);

        // Overwrite on: CO is replaced, OFF stays protected.
        var on = await svc.PreviewAsync(Req("E1", "2026-09-21", "2026-09-25") with { Overwrite = true });
        Assert.Equal("2026-09-22", Assert.Single(on.Replacements).Date);
        Assert.Equal("CO", Assert.Single(on.Replacements).ExistingShiftType);
        Assert.Equal("2026-09-23", Assert.Single(on.Collisions).Date);
    }

    // -- generate ------------------------------------------------------------

    [Fact]
    public async Task Generate_CreatesWorkingRows_TaggedWithBatch()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        await db.SaveChangesAsync();

        var r = await NewSvc(db).GenerateAsync(Req("E1", "2026-09-21", "2026-09-27"), "K1", "Tester");

        Assert.Equal(5, r.Created);
        Assert.Equal(0, r.SkippedExisting);
        Assert.Equal("2026-09-21", r.FirstDate);
        Assert.Equal("2026-09-25", r.LastDate);

        var rows = await db.ShiftEntries.Where(x => x.EmployeeId == "E1").ToListAsync();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, x =>
        {
            Assert.Equal(RosterService.SourceModuleName, x.SourceModule);
            Assert.Equal(r.BatchId, x.SourceId);
            Assert.Equal(ShiftTypes.Working, x.ShiftType);
            Assert.Equal("08:00 - 16:30", x.RawValue);
            Assert.True(x.AutoGenerated);
        });

        var batch = await db.RosterBatches.SingleAsync();
        Assert.Equal(r.BatchId, batch.Id);
        Assert.Equal(5, batch.RowCount);
        Assert.Equal("K1", batch.CreatedByKid);
        Assert.Equal("Tester", batch.CreatedByName);
        Assert.Null(batch.DeletedAt);
    }

    [Fact]
    public async Task Generate_ExistingRow_IsSkipped_NeverOverwritten()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "E1", ShiftDate = new DateOnly(2026, 9, 22), ShiftType = "AL", SourceModule = "Vacation" });
        await db.SaveChangesAsync();

        var r = await NewSvc(db).GenerateAsync(Req("E1", "2026-09-21", "2026-09-25"), null, null);

        Assert.Equal(4, r.Created);
        Assert.Equal(1, r.SkippedExisting);
        // The pre-existing row is byte-identical afterwards.
        var stillThere = await db.ShiftEntries.SingleAsync(x => x.ShiftDate == new DateOnly(2026, 9, 22));
        Assert.Equal("AL", stillThere.ShiftType);
        Assert.Equal("Vacation", stillThere.SourceModule);
        Assert.Null(stillThere.SourceId);
    }

    [Fact]
    public async Task Generate_InvalidEmployee_Throws()
    {
        using var db = NewDb();
        await Assert.ThrowsAsync<RosterValidationException>(
            () => NewSvc(db).GenerateAsync(Req("NOPE", "2026-09-21", "2026-09-25"), null, null));
    }

    // -- missing-roster: per-employee date gaps ---------------------------------

    [Fact]
    public async Task GetMissingRoster_ReportsPerEmployeeDateGaps_ForSparseCoverage()
    {
        using var db = NewDb();
        SeedEmployee(db, "FULL1");
        SeedEmployee(db, "SPARSE1");
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        // FULL1 has a row every day of the 3-day window.
        for (var d = today; d <= today.AddDays(2); d = d.AddDays(1))
            db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "FULL1", ShiftDate = d, ShiftType = ShiftTypes.Working });
        // SPARSE1 only has a row on day 1 — days 2 and 3 are gaps, but he's not
        // "fully missing" (he has at least one row), so the old MissingRoster
        // check can't see this.
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "SPARSE1", ShiftDate = today, ShiftType = ShiftTypes.Working });
        await db.SaveChangesAsync();

        var result = await NewSvc(db).GetMissingRosterAsync(3);

        Assert.Equal(3, result.Days);
        Assert.DoesNotContain(result.MissingRoster, m => m.EmployeeId == "SPARSE1"); // old field blind to this
        var gap = Assert.Single(result.DateGapsByEmployee);
        Assert.Equal("SPARSE1", gap.EmployeeId);
        Assert.Equal(
            new[] { today.AddDays(1).ToString("yyyy-MM-dd"), today.AddDays(2).ToString("yyyy-MM-dd") },
            gap.MissingDates);
        Assert.DoesNotContain(result.DateGapsByEmployee, g => g.EmployeeId == "FULL1");

        // Clamp raised to 90 (was 60).
        var clamped = await NewSvc(db).GetMissingRosterAsync(200);
        Assert.Equal(90, clamped.Days);
    }

    // -- location coverage: MAIN/BACKUP walk ------------------------------------
    // Reproduces the Hamburg bug: MAIN is already on WIC duty at a different
    // centre with overlapping hours (unavailable); BACKUP is free (a plain
    // working shift with no task, same shape as the real PATTERN_FILL rows).
    // GenerateLocationCoverageAsync must walk past the unavailable MAIN and
    // assign the free BACKUP instead of leaving the day a gap.

    [Fact]
    public async Task GenerateLocationCoverage_AssignsFreeBackup_WhenMainIsUnavailable()
    {
        using var db = NewDb();
        var monday = new DateOnly(2026, 9, 21); // Monday, see helper comment above

        var target = new WicLocation { LocationCode = "TARGET", DisplayName = "Target Centre", IsActive = true };
        var other  = new WicLocation { LocationCode = "OTHER",  DisplayName = "Other Centre",  IsActive = true };
        db.WicLocations.AddRange(target, other);
        // Target open Mon 09:00-17:00; Other open Mon 08:00-12:00 (overlaps Target's hours).
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "TARGET", DayOfWeek = 1, OpenTime = "09:00", CloseTime = "17:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "OTHER",  DayOfWeek = 1, OpenTime = "08:00", CloseTime = "12:00" });

        var main = SeedEmployee(db, "MAIN1");
        main.FullName = "Main Agent";
        var backup = SeedEmployee(db, "BACKUP1");
        backup.FullName = "Backup Agent";
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = "TARGET", EmployeeName = "Main Agent", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = "TARGET", EmployeeName = "Backup Agent", AssignmentType = "BACKUP", IsActive = true });

        // MAIN already on WIC duty elsewhere, overlapping Target's hours -> unavailable.
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "MAIN1", ShiftDate = monday, ShiftType = ShiftTypes.WicDuty, AgentTask = "Other Centre", IsWicDuty = true });
        db.WicShiftEntries.Add(new WicShiftEntry { EmployeeId = "MAIN1", ShiftDate = monday, SupportLocation = "Other Centre", IsOnSite = true, WorkingShift = "08:00-12:00", Task = "WIC" });
        // BACKUP free: plain working shift, no task.
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "BACKUP1", ShiftDate = monday, ShiftType = ShiftTypes.Working, RawValue = "08:00 - 16:30" });
        await db.SaveChangesAsync();

        var result = await NewSvc(db).GenerateLocationCoverageAsync("TARGET", monday, monday, "K1", "Tester");

        Assert.Equal(0, result.DaysStillGap);
        Assert.False(result.CountMismatch);
        var assigned = Assert.Single(result.Assignments);
        Assert.Equal("BACKUP1", assigned.EmployeeId);
        Assert.Equal("BACKUP", assigned.AssignmentType);
        Assert.Equal(1, assigned.DaysAssigned);

        var backupShift = await db.ShiftEntries.SingleAsync(x => x.EmployeeId == "BACKUP1" && x.ShiftDate == monday);
        Assert.Equal(ShiftTypes.WicDuty, backupShift.ShiftType);
        Assert.Equal("Target Centre", backupShift.AgentTask);
    }

    // A stale on-site WicShiftEntries row must not count as coverage when the same
    // employee's ShiftEntries says a full absence that day (the Bishal Maharjan/
    // Hamburg case: a vacation import set AL but never cleared IsOnSite=1). Before
    // the fix, ComputeCoveredDates trusted the on-site row blindly, marked the day
    // "already covered", and never even tried the free BACKUP.
    [Fact]
    public async Task GenerateLocationCoverage_StaleOnSiteRow_DoesNotCountAsCovered_WhenEmployeeIsAbsent()
    {
        using var db = NewDb();
        var monday = new DateOnly(2026, 9, 21); // Monday

        var target = new WicLocation { LocationCode = "TARGET3", DisplayName = "Target Centre 3", IsActive = true };
        db.WicLocations.Add(target);
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "TARGET3", DayOfWeek = 1, OpenTime = "09:00", CloseTime = "17:00" });

        var main = SeedEmployee(db, "STALE1");
        main.FullName = "Stale Agent";
        var backup = SeedEmployee(db, "BACKUP3");
        backup.FullName = "Backup Agent 3";
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = "TARGET3", EmployeeName = "Stale Agent", AssignmentType = "MAIN", IsActive = true });
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = "TARGET3", EmployeeName = "Backup Agent 3", AssignmentType = "BACKUP", IsActive = true });

        // MAIN is on AL, but a stale WicShiftEntries row still says IsOnSite=1.
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "STALE1", ShiftDate = monday, ShiftType = "AL" });
        db.WicShiftEntries.Add(new WicShiftEntry { EmployeeId = "STALE1", ShiftDate = monday, SupportLocation = "Target Centre 3", IsOnSite = true, WorkingShift = "09:00-17:00", Task = "WIC" });
        // BACKUP free: plain working shift, no task.
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "BACKUP3", ShiftDate = monday, ShiftType = ShiftTypes.Working, RawValue = "08:00 - 16:30" });
        await db.SaveChangesAsync();

        var result = await NewSvc(db).GenerateLocationCoverageAsync("TARGET3", monday, monday, "K1", "Tester");

        Assert.Equal(0, result.DaysAlreadyCovered); // the stale on-site row must not count
        Assert.Equal(0, result.DaysStillGap);
        var assigned = Assert.Single(result.Assignments);
        Assert.Equal("BACKUP3", assigned.EmployeeId);
        Assert.Equal("BACKUP", assigned.AssignmentType);
    }

    // A candidate with NO ShiftEntries row at all on the date is UNKNOWN, not
    // verified free (an absence that was never imported can't be ruled out) -
    // the walk must not roster someone on that guess. It must be left a gap.
    [Fact]
    public async Task GenerateLocationCoverage_NoRecordAtAll_IsTreatedAsUnavailable_NotAssigned()
    {
        using var db = NewDb();
        var monday = new DateOnly(2026, 9, 21); // Monday

        var target = new WicLocation { LocationCode = "TARGET2", DisplayName = "Target Centre 2", IsActive = true };
        db.WicLocations.Add(target);
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = "TARGET2", DayOfWeek = 1, OpenTime = "09:00", CloseTime = "17:00" });

        var main = SeedEmployee(db, "MAIN2");
        main.FullName = "Main Agent 2";
        db.WicAgentAssignments.Add(new WicAgentAssignment { LocationCode = "TARGET2", EmployeeName = "Main Agent 2", AssignmentType = "MAIN", IsActive = true });
        // Deliberately no ShiftEntries row at all for MAIN2 on `monday`.
        await db.SaveChangesAsync();

        var result = await NewSvc(db).GenerateLocationCoverageAsync("TARGET2", monday, monday, "K1", "Tester");

        Assert.Equal(1, result.DaysStillGap);
        Assert.Empty(result.Assignments);
        Assert.False(await db.ShiftEntries.AnyAsync(x => x.EmployeeId == "MAIN2" && x.ShiftDate == monday));
    }

    // -- delete a batch --------------------------------------------------------

    [Fact]
    public async Task DeleteBatch_RemovesOwnedRows_SoftDeletesBatch()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        await db.SaveChangesAsync();
        var svc = NewSvc(db);
        var g = await svc.GenerateAsync(Req("E1", "2026-09-21", "2026-09-25"), null, null);

        var d = await svc.DeleteBatchAsync(g.BatchId);

        Assert.NotNull(d);
        Assert.Equal(5, d!.DeletedRows);
        Assert.Equal(0, d.DetachedRows);
        Assert.Empty(await db.ShiftEntries.Where(x => x.EmployeeId == "E1").ToListAsync());

        // Soft-deleted: the batch row survives for the audit trail but leaves the active list.
        var batch = await db.RosterBatches.SingleAsync(b => b.Id == g.BatchId);
        Assert.NotNull(batch.DeletedAt);
        Assert.Empty(await svc.GetBatchesAsync());

        // Second delete: batch already gone.
        Assert.Null(await svc.DeleteBatchAsync(g.BatchId));
    }

    [Fact]
    public async Task DeleteBatch_DetachedRows_AreLeftUntouched()
    {
        using var db = NewDb();
        SeedEmployee(db, "E1");
        await db.SaveChangesAsync();
        var svc = NewSvc(db);
        var g = await svc.GenerateAsync(Req("E1", "2026-09-21", "2026-09-25"), null, null);

        // Another module takes over one row (e.g. ShiftSyncService marks sick leave).
        var taken = await db.ShiftEntries.FirstAsync(x => x.SourceId == g.BatchId);
        taken.SourceModule = "SickLeave";
        taken.ShiftType = "SL";
        await db.SaveChangesAsync();

        var d = await svc.DeleteBatchAsync(g.BatchId);

        Assert.Equal(4, d!.DeletedRows);
        Assert.Equal(1, d.DetachedRows);
        var survivor = Assert.Single(await db.ShiftEntries.Where(x => x.EmployeeId == "E1").ToListAsync());
        Assert.Equal("SL", survivor.ShiftType);
        Assert.Equal("SickLeave", survivor.SourceModule);
    }

    // -- role gate (always on, independent of Auth:EnforceAuthorization) -------

    private static HttpContext CtxWithRole(string? role)
    {
        var ctx = new DefaultHttpContext();
        if (role != null)
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(AuthClaims.Role, role) }, authenticationType: "test");
            ctx.User = new ClaimsPrincipal(identity);
        }
        return ctx;
    }

    [Theory]
    [InlineData(AppRoles.Rtm)]
    [InlineData(AppRoles.TeamLead)]
    [InlineData(AppRoles.Dev)]
    public void RoleGate_RtmTeamLeadDev_AreAllowed(string role)
    {
        Assert.Null(RosterAccess.DenyResult(CtxWithRole(role)));
    }

    [Fact]
    public void RoleGate_Agent_Gets403()
    {
        var deny = RosterAccess.DenyResult(CtxWithRole(AppRoles.Agent));
        var res = Assert.IsAssignableFrom<IStatusCodeHttpResult>(deny);
        Assert.Equal(StatusCodes.Status403Forbidden, res.StatusCode);
    }

    [Fact]
    public void RoleGate_Anonymous_Gets401()
    {
        var deny = RosterAccess.DenyResult(CtxWithRole(null));
        var res = Assert.IsAssignableFrom<IStatusCodeHttpResult>(deny);
        Assert.Equal(StatusCodes.Status401Unauthorized, res.StatusCode);
    }
}
