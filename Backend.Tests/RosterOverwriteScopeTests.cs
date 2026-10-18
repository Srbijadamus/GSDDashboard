using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Roster;
using GSDDashboard.API.Modules.WicShifts;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GSDDashboard.Tests;

// Read-only scope analysis, pinned as an in-memory test.
//
// With Overwrite=true the roster generator may replace ONLY rows that
// RosterService.IsProtectedFromOverwrite does not refuse. Protection is decided
// purely by ShiftType + AgentTask + ManualChangeBy — never by provenance
// (SourceModule / SourceId / the SYSTEM_* roster batch that wrote the row).
//
//   protected (da -> ne, cannot be replaced):
//     AL / HALF_AL / SL / UL / PH / LPH / OFF / OFF_WEEKEND / OL / RESIGNED / CD
//     TRAINING, WORKING+VWIC, any row with ManualChangeBy set, and any WIC_DUTY
//     (or WORKING-with-a-centre-in-AgentTask) row whose centre is NOT the one
//     being generated.
//   replaceable (da):
//     WORKING / BO / EMPTY / CO, and a WIC_DUTY row when this run targets the
//     SAME centre (the "same centre as AgentTask" exception).
//
// No real employee names and no real centre names are used here.
public class RosterOverwriteScopeTests
{
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static readonly DateOnly Tue = new(2026, 10, 6);
    private static readonly DateOnly Wed = new(2026, 10, 7);
    private static readonly DateOnly Thu = new(2026, 10, 8);
    private static readonly DateOnly Fri = new(2026, 10, 9);

    private const string CentreA = "CTR-A";
    private const string CentreAName = "Centre A";
    private const string CentreB = "CTR-B";
    private const string CentreBName = "Centre B";

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // GenerateAsync runs in a transaction; the in-memory store has none.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RosterService NewSvc(GSDContext db) =>
        new(db, new WicShiftService(db, new AvailabilityResolver(db)), new WicCoverageGapService(db));

    private static void SeedAgent(GSDContext db, string id = "E1")
    {
        db.Employees.Add(new Employee { EmployeeId = id, FullName = "Agent " + id, IsActive = true });
    }

    private static void SeedCentre(GSDContext db, string code, string name)
    {
        db.WicLocations.Add(new WicLocation
        {
            LocationCode = code, DisplayName = name, MinAgentsRequired = 1, IsActive = true,
        });
        for (var dow = 0; dow <= 6; dow++)
            db.WicOpeningHours.Add(new WicOpeningHour
            {
                LocationCode = code,
                DayOfWeek = dow,
                IsClosed = dow is 0 or 6,
                OpenTime = dow is 0 or 6 ? null : "09:00",
                CloseTime = dow is 0 or 6 ? null : "17:00",
            });
    }

    private static RosterRequest WicRequest(bool overwrite = true, string from = "2026-10-05", string to = "2026-10-09") =>
        new("E1", from, to, "08:00", "17:00", null, null, CentreA, overwrite);

    // ── The requested period test ───────────────────────────────────────────────

    // Period Mon-Fri, Overwrite=true, WIC mode for Centre A, with one existing row
    // per day:
    //   Mon  AL                     (Vacations sync)      -> stays
    //   Tue  SL                     (SickLeave sync)      -> stays
    //   Wed  WORKING + ManualChangeBy (hand edit)         -> stays
    //   Thu  WIC_DUTY at Centre A   (old SYSTEM_* batch)  -> REPLACED (same centre)
    //   Fri  WIC_DUTY at Centre B   (old SYSTEM_* batch)  -> stays (other centre)
    [Fact]
    public async Task OverwritePeriod_KeepsAL_SL_ManualAndOtherCentre_ReplacesOnlySameCentreSystemWicDuty()
    {
        using var db = NewDb();
        SeedAgent(db);
        SeedCentre(db, CentreA, CentreAName);
        SeedCentre(db, CentreB, CentreBName);

        // The provenance of the two WIC_DUTY rows: an old "SYSTEM_*" roster batch.
        var systemBatch = new RosterBatch
        {
            EmployeeId = "E1", FullName = "Agent E1", DateFrom = Mon, DateTo = Fri,
            CreatedByKid = "SYSTEM_WICFIX", CreatedByName = "system",
            RowCount = 2, CreatedAt = new DateTime(2026, 9, 30, 6, 9, 0, DateTimeKind.Utc),
        };
        db.RosterBatches.Add(systemBatch);

        db.ShiftEntries.AddRange(
            new ShiftEntry { EmployeeId = "E1", ShiftDate = Mon, ShiftType = "AL", SourceModule = "Vacation" },
            new ShiftEntry { EmployeeId = "E1", ShiftDate = Tue, ShiftType = "SL", SourceModule = "SickLeave" },
            new ShiftEntry
            {
                EmployeeId = "E1", ShiftDate = Wed, ShiftType = ShiftTypes.Working, RawValue = "BO",
                SourceModule = "Roster", ManualChangeBy = "ACTOR-1",
                ManualChangeAt = new DateTime(2026, 10, 1, 7, 5, 0, DateTimeKind.Utc),
            },
            new ShiftEntry
            {
                EmployeeId = "E1", ShiftDate = Thu, ShiftType = ShiftTypes.WicDuty, AgentTask = CentreAName,
                IsWicDuty = true, SourceModule = "Roster", SourceSheet = "ROSTER", SourceId = systemBatch.Id,
            },
            new ShiftEntry
            {
                EmployeeId = "E1", ShiftDate = Fri, ShiftType = ShiftTypes.WicDuty, AgentTask = CentreBName,
                IsWicDuty = true, SourceModule = "Roster", SourceSheet = "ROSTER", SourceId = systemBatch.Id,
            });
        await db.SaveChangesAsync();
        var systemBatchId = systemBatch.Id;

        var svc = NewSvc(db);

        // ── Preview: exactly one replacement, four protected collisions ─────────
        var preview = await svc.PreviewAsync(WicRequest());

        var replaced = Assert.Single(preview.Replacements);
        Assert.Equal("2026-10-08", replaced.Date);
        Assert.Equal(ShiftTypes.WicDuty, replaced.ExistingShiftType);
        Assert.Equal("WIC", replaced.Kind);

        Assert.Equal(4, preview.Collisions.Count);
        Assert.All(preview.Collisions, c => Assert.True(c.Protected));
        Assert.Equal(
            new[] { "2026-10-05", "2026-10-06", "2026-10-07", "2026-10-09" },
            preview.Collisions.Select(c => c.Date).OrderBy(d => d).ToArray());
        Assert.Equal(0, preview.RowCount);          // no NEW row is created
        Assert.Equal(1, preview.ReplaceCount);
        var planned = Assert.Single(preview.Rows);  // Rows = create + replace
        Assert.Equal("2026-10-08", planned.Date);
        Assert.Equal("WIC", planned.Kind);

        // ── Generate: the same one replacement ──────────────────────────────────
        var gen = await svc.GenerateAsync(WicRequest(), "K1", "Tester");
        Assert.Equal(0, gen.Created);
        Assert.Equal(1, gen.Replaced);

        var rows = await db.ShiftEntries.Where(x => x.EmployeeId == "E1").ToListAsync();
        Assert.Equal(5, rows.Count);   // nothing was deleted or duplicated

        var al = rows.Single(x => x.ShiftDate == Mon);
        Assert.Equal("AL", al.ShiftType);
        Assert.Equal("Vacation", al.SourceModule);
        Assert.Null(al.SourceId);

        var sl = rows.Single(x => x.ShiftDate == Tue);
        Assert.Equal("SL", sl.ShiftType);
        Assert.Equal("SickLeave", sl.SourceModule);
        Assert.Null(sl.SourceId);

        var manual = rows.Single(x => x.ShiftDate == Wed);
        Assert.Equal(ShiftTypes.Working, manual.ShiftType);
        Assert.Equal("BO", manual.RawValue);
        Assert.Equal("ACTOR-1", manual.ManualChangeBy);

        // The same-centre SYSTEM_* WIC_DUTY row was rewritten in place by the batch.
        var sameCentre = rows.Single(x => x.ShiftDate == Thu);
        Assert.Equal(ShiftTypes.WicDuty, sameCentre.ShiftType);
        Assert.Equal(CentreAName, sameCentre.AgentTask);
        Assert.Equal(RosterService.SourceModuleName, sameCentre.SourceModule);
        Assert.Equal(gen.BatchId, sameCentre.SourceId);
        Assert.True(sameCentre.AutoGenerated);
        Assert.Null(sameCentre.ManualChangeBy);

        // The other-centre WIC_DUTY row is untouched: still owned by the old batch.
        var otherCentre = rows.Single(x => x.ShiftDate == Fri);
        Assert.Equal(ShiftTypes.WicDuty, otherCentre.ShiftType);
        Assert.Equal(CentreBName, otherCentre.AgentTask);
        Assert.Equal(systemBatchId, otherCentre.SourceId);

        // The old SYSTEM_* batch itself was not touched either.
        Assert.Null((await db.RosterBatches.SingleAsync(b => b.Id == systemBatchId)).DeletedAt);
    }

    // ── The same-centre exception, in isolation ─────────────────────────────────

    [Fact]
    public async Task SameCentreException_ReplacesWicDutyAtTheGeneratedCentre_ButNotAtAnother()
    {
        using var db = NewDb();
        SeedAgent(db);
        SeedCentre(db, CentreA, CentreAName);
        SeedCentre(db, CentreB, CentreBName);
        db.ShiftEntries.AddRange(
            new ShiftEntry { EmployeeId = "E1", ShiftDate = Mon, ShiftType = ShiftTypes.WicDuty, AgentTask = CentreAName, IsWicDuty = true },
            new ShiftEntry { EmployeeId = "E1", ShiftDate = Tue, ShiftType = ShiftTypes.WicDuty, AgentTask = CentreBName, IsWicDuty = true });
        await db.SaveChangesAsync();

        var preview = await NewSvc(db).PreviewAsync(WicRequest());

        var replaced = Assert.Single(preview.Replacements);
        Assert.Equal("2026-10-05", replaced.Date);
        var collision = Assert.Single(preview.Collisions);
        Assert.Equal("2026-10-06", collision.Date);
        Assert.True(collision.Protected);   // a WIC commitment elsewhere survives
    }

    [Fact]
    public async Task WicDutyInPlainMode_IsProtected_EvenWhenItsCentreWouldMatchNothing()
    {
        using var db = NewDb();
        SeedAgent(db);
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Mon, ShiftType = ShiftTypes.WicDuty,
            AgentTask = CentreAName, IsWicDuty = true,
        });
        await db.SaveChangesAsync();

        // Plain roster: no LocationCode, no MAIN -> targetLocation is null.
        var preview = await NewSvc(db).PreviewAsync(
            new RosterRequest("E1", "2026-10-05", "2026-10-09", "08:00", "17:00", null, null, null, true));

        Assert.Empty(preview.Replacements);
        Assert.True(Assert.Single(preview.Collisions).Protected);
    }

    // The flipped shape the SYSTEM_WICFIX batches left behind: ShiftType=WORKING but
    // AgentTask holds a centre name. The same-centre exception treats it like WIC_DUTY;
    // a real task badge (GSD) is NOT a WIC commitment and is simply replaceable.
    [Fact]
    public async Task FlippedWorkingRow_NamingTheCentre_IsReplaced_ATaskBadgeRowIsAlsoReplaced()
    {
        using var db = NewDb();
        SeedAgent(db);
        SeedCentre(db, CentreA, CentreAName);
        db.ShiftEntries.AddRange(
            new ShiftEntry { EmployeeId = "E1", ShiftDate = Mon, ShiftType = ShiftTypes.Working, AgentTask = CentreAName, SourceModule = "Roster" },
            new ShiftEntry { EmployeeId = "E1", ShiftDate = Tue, ShiftType = ShiftTypes.Working, AgentTask = "GSD", SourceModule = "Roster" });
        await db.SaveChangesAsync();

        var preview = await NewSvc(db).PreviewAsync(WicRequest());

        Assert.Equal(2, preview.Replacements.Count);
        Assert.Equal("WORKING", preview.Replacements.Single(r => r.Date == "2026-10-05").ExistingShiftType);
        Assert.Equal("WORKING", preview.Replacements.Single(r => r.Date == "2026-10-06").ExistingShiftType);
        Assert.Empty(preview.Collisions);
    }

    // The manual marker is checked FIRST: it wins over the same-centre exception.
    [Fact]
    public async Task ManualMarker_WinsOverTheSameCentreException()
    {
        using var db = NewDb();
        SeedAgent(db);
        SeedCentre(db, CentreA, CentreAName);
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "E1", ShiftDate = Mon, ShiftType = ShiftTypes.WicDuty, AgentTask = CentreAName,
            IsWicDuty = true, ManualChangeBy = "ACTOR-1",
        });
        await db.SaveChangesAsync();

        var preview = await NewSvc(db).PreviewAsync(WicRequest());

        Assert.Empty(preview.Replacements);
        Assert.True(Assert.Single(preview.Collisions).Protected);

        var gen = await NewSvc(db).GenerateAsync(WicRequest(), "K1", "Tester");
        Assert.Equal(0, gen.Replaced);
        var row = await db.ShiftEntries.SingleAsync(x => x.EmployeeId == "E1" && x.ShiftDate == Mon);
        Assert.Equal("ACTOR-1", row.ManualChangeBy);
        Assert.Equal(ShiftTypes.WicDuty, row.ShiftType);
        Assert.Null(row.SourceId);   // never rewritten by the batch
    }
}
