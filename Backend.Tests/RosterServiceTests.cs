using System.Security.Claims;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Auth;
using GSDDashboard.API.Modules.Roster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
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
            .Options;
        return new GSDContext(options);
    }

    private static RosterService NewSvc(GSDContext db) => new(db);

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
