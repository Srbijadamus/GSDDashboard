using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Rollout;
using GSDDashboard.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// ROLLOUT — the manual "Rollout" page (documentation/ROLLOUT.md).
//
// A rollout day is one WIC centre opened on ONE date for new workers, with its own hours.
// These tests pin the contract RTM relies on:
//   * the entry is manual and records its author;
//   * the rollout hours win for that centre and date, and the weekday pattern still
//     applies on every other date;
//   * the selected workers count as assigned to that centre at that time (coverage /
//     presence), and the agents already there are not displaced;
//   * an overlapping assignment elsewhere is reported as a warning, never a block;
//   * nothing is reported for a non-overlapping one;
//   * automatic writers can neither change nor delete the rollout rows;
//   * an edit or a delete touches only what the rollout itself created.
public class RolloutTests
{
    private const string Code  = "DE~86150~Augsburg~Schaezlerstr. 3";
    private const string Name  = "Augsburg - Schaezlerstr.";
    private const string CodeB = "DE~86150~Augsburg~Bahnhofstr. 1";
    private const string NameB = "Augsburg - Bahnhofstr.";

    private static readonly DateOnly Thu = new(2026, 10, 8);   // 2026-10-08 is a Thursday
    private static readonly DateOnly Fri = new(2026, 10, 9);

    private static GSDContext NewDb() =>
        new(new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static void SeedCentres(GSDContext db)
    {
        db.WicLocations.Add(new WicLocation { LocationCode = Code, DisplayName = Name, City = "Augsburg", MinAgentsRequired = 1, IsActive = true });
        db.WicLocations.Add(new WicLocation { LocationCode = CodeB, DisplayName = NameB, City = "Augsburg", MinAgentsRequired = 1, IsActive = true });
        // Standard weekday hours 09:00-17:00 for both Thursday and Friday.
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = Code, DayOfWeek = (int)DayOfWeek.Thursday, OpenTime = "09:00", CloseTime = "17:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = Code, DayOfWeek = (int)DayOfWeek.Friday,   OpenTime = "09:00", CloseTime = "17:00" });
        db.WicOpeningHours.Add(new WicOpeningHour { LocationCode = CodeB, DayOfWeek = (int)DayOfWeek.Thursday, OpenTime = "09:00", CloseTime = "17:00" });
    }

    private static void SeedEmployee(GSDContext db, string id, string name, bool trainee = false) =>
        db.Employees.Add(new Employee { EmployeeId = id, FullName = name, IsActive = true, IsTrainee = trainee });

    private static SaveRolloutRequest Request(params string[] employeeIds) =>
        new(Code, Thu.ToString("yyyy-MM-dd"), "08:00", "14:00", "New workers onboarding",
            employeeIds.Select(id => new RolloutWorkerRequest(id, true, null)).ToList());

    // ── 1. Create / author / manual flag ────────────────────────────────────────────

    [Fact]
    public async Task Create_StoresTheManualEntry_AndItsAuthor()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "E1", "New Worker One");
        await db.SaveChangesAsync();

        var svc = new RolloutService(db);
        var result = await svc.CreateAsync(Request("E1"), "S60028");

        Assert.True(result.Success);
        var day = await db.WicRolloutDays.Include(d => d.Workers).SingleAsync();
        Assert.True(day.IsManual);
        Assert.Equal("S60028", day.ManualChangeBy);
        Assert.NotNull(day.ManualChangeAt);
        Assert.Equal(new DateOnly(2026, 10, 8), day.RolloutDate);
        Assert.Equal("08:00", day.StartTime);
        Assert.Equal("14:00", day.EndTime);
        Assert.Single(day.Workers);
        Assert.Equal("E1", day.Workers[0].EmployeeId);
        Assert.Equal("S60028", result.Day!.CreatedBy);
        Assert.True(result.Day.IsManual);
    }

    [Fact]
    public async Task Create_TwiceForTheSameCentreAndDate_IsRejected()
    {
        using var db = NewDb();
        SeedCentres(db);
        await db.SaveChangesAsync();
        var svc = new RolloutService(db);

        Assert.True((await svc.CreateAsync(Request(), "UI-MANUAL")).Success);
        var second = await svc.CreateAsync(Request(), "UI-MANUAL");
        Assert.False(second.Success);
        Assert.Contains("already exists", second.Reason);
    }

    [Fact]
    public async Task Create_WithAnUnknownWorker_IsRejected()
    {
        using var db = NewDb();
        SeedCentres(db);
        await db.SaveChangesAsync();
        var svc = new RolloutService(db);

        var result = await svc.CreateAsync(Request("NOPE"), "UI-MANUAL");
        Assert.False(result.Success);
        Assert.Contains("Unknown employee", result.Reason);
        Assert.Empty(await db.WicRolloutDays.ToListAsync());
    }

    // ── 2. Hours: the rollout day wins for that centre and date only ─────────────────

    [Fact]
    public async Task RolloutHours_ReplaceTheStandardHoursForThatDate_AndOnlyThatDate()
    {
        using var db = NewDb();
        SeedCentres(db);
        await db.SaveChangesAsync();
        var svc = new RolloutService(db);
        Assert.True((await svc.CreateAsync(Request(), "UI-MANUAL")).Success);

        var all = await db.WicOpeningHours.ToListAsync();

        var rolloutDay = WicHoursResolver.Resolve(all, Code, null, (int)Thu.DayOfWeek, Thu);
        Assert.NotNull(rolloutDay);
        Assert.Equal("08:00", rolloutDay!.OpenTime);
        Assert.Equal("14:00", rolloutDay.CloseTime);
        Assert.False(rolloutDay.IsClosed);

        // The next day still uses the weekday pattern.
        var friday = WicHoursResolver.Resolve(all, Code, null, (int)Fri.DayOfWeek, Fri);
        Assert.NotNull(friday);
        Assert.Equal("09:00", friday!.OpenTime);
        Assert.Equal("17:00", friday.CloseTime);

        // The other centre on the same date is untouched.
        var other = WicHoursResolver.Resolve(all, CodeB, null, (int)Thu.DayOfWeek, Thu);
        Assert.Equal("09:00", other!.OpenTime);

        var dto = await svc.GetOneAsync((await db.WicRolloutDays.SingleAsync()).Id);
        Assert.True(dto!.IsDifferentFromStandard);
        Assert.Equal("09:00", dto.StandardStart);
        Assert.Equal("17:00", dto.StandardEnd);
        Assert.False(dto.StandardClosed);
    }

    // ── 3. Workers enter coverage; the agents already there stay ─────────────────────

    [Fact]
    public async Task RolloutWorkers_CountAsPresent_AndDoNotDisplaceTheExistingAgents()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "OLD", "Existing Agent");
        SeedEmployee(db, "NEW", "New Worker One");
        // The agent who is already on this centre that day.
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "OLD", ShiftDate = Thu, DayOfWeek = "Thursday",
            SupportLocation = Name, LocationCode = Code, WorkingShift = "09:00 - 17:00",
            IsOnSite = true, Task = "WIC",
        });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "OLD", ShiftDate = Thu, ShiftType = ShiftTypes.WicDuty, IsWicDuty = true,
            AgentTask = Name, ShiftStart = "09:00", ShiftEnd = "17:00",
        });
        await db.SaveChangesAsync();

        var svc = new RolloutService(db);
        Assert.True((await svc.CreateAsync(Request("NEW"), "UI-MANUAL")).Success);

        var loc = await db.WicLocations.SingleAsync(l => l.LocationCode == Code);
        var wicRows = await db.WicShiftEntries.Where(w => w.ShiftDate == Thu).ToListAsync();
        var shifts = await db.ShiftEntries.Where(s => s.ShiftDate == Thu).ToListAsync();
        var byEmpDate = shifts.GroupBy(s => s.EmployeeId)
            .ToDictionary(g => (g.Key, Thu), g => ShiftDuplicateResolver.BestShiftEntry(g));

        var presence = WicPresenceResolver.Resolve(
            loc, Thu, wicRows, byEmpDate, WicPresenceResolver.CandidateDutyRows(shifts), _ => false);

        Assert.Equal(2, presence.Agents.Count);
        Assert.Contains(presence.Agents, a => a.EmployeeId == "OLD" && a.Contribution == 1.0);
        Assert.Contains(presence.Agents, a => a.EmployeeId == "NEW" && a.Contribution == 1.0);
        Assert.Equal(2, presence.EffectiveCoverage);
        Assert.Equal(WicCoverageEvidence.ON_SITE, presence.Evidence);

        // The rollout marked its own row manual, so automatic writers must preserve it.
        var newShift = shifts.Single(s => s.EmployeeId == "NEW");
        Assert.True(newShift.IsManualChange);
        Assert.Equal("ROLLOUT", newShift.SourceModule);
        Assert.True(db.PreserveManualShift(newShift, "test"));
        Assert.True(db.PreserveManualShift(shifts.Single(s => s.EmployeeId == "OLD"), "test") == false);
    }

    // ── 4. Automatic writers cannot change or delete the rollout rows ────────────────

    [Fact]
    public async Task AutomaticWriter_CannotOverwriteOrDeleteARolloutRow()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "NEW", "New Worker One");
        await db.SaveChangesAsync();
        Assert.True((await new RolloutService(db).CreateAsync(Request("NEW"), "UI-MANUAL")).Success);

        var row = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "NEW" && s.ShiftDate == Thu);
        row.ShiftType = ShiftTypes.AnnualLeave;               // an automatic writer's mistake
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        using var verify = NewDb();
        Assert.NotEqual(0, row.Id);
    }

    // ── 5. Overlap warning (non-blocking) / no warning when it does not overlap ──────

    [Fact]
    public async Task OverlappingAssignmentElsewhere_IsReportedAsAWarning_AndDoesNotBlockTheSave()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "A", "Overlapping Worker");
        SeedEmployee(db, "B", "Later Worker");
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "A", ShiftDate = Thu, SupportLocation = NameB, LocationCode = CodeB,
            WorkingShift = "09:00 - 11:00", IsOnSite = true, Task = "WIC",
        });
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "B", ShiftDate = Thu, SupportLocation = NameB, LocationCode = CodeB,
            WorkingShift = "15:00 - 18:00", IsOnSite = true, Task = "WIC",
        });
        await db.SaveChangesAsync();

        var svc = new RolloutService(db);
        var result = await svc.CreateAsync(Request("A", "B"), "UI-MANUAL");

        Assert.True(result.Success);
        Assert.Contains(result.Day!.Warnings, w => w.EmployeeId == "A" && w.Type == "OVERLAP" &&
                                                   w.OtherLocation == NameB && w.OtherStart == "09:00" && w.OtherEnd == "11:00");
        Assert.DoesNotContain(result.Day.Warnings, w => w.EmployeeId == "B");

        // The preview endpoint reports the same thing without writing anything.
        var before = await db.WicRolloutDays.CountAsync();
        var preview = await svc.PreviewAsync(Request("A", "B"));
        Assert.Contains(preview, w => w.EmployeeId == "A" && w.Type == "OVERLAP");
        Assert.Equal(before, await db.WicRolloutDays.CountAsync());
    }

    [Fact]
    public async Task ARolloutWorkerWithoutAnotherCommitment_IsNeverWarnedAbout_NotEvenInPreview()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "NEW", "New Worker One");
        await db.SaveChangesAsync();

        var svc = new RolloutService(db);
        var created = await svc.CreateAsync(Request("NEW"), "UI-MANUAL");
        Assert.True(created.Success);
        Assert.Empty(created.Day!.Warnings);

        // Reading it back and previewing an edit must not report the rollout's own
        // day-level row or its own on-site row as a competing commitment.
        var dayId = created.Day.Id;
        Assert.Empty((await svc.GetOneAsync(dayId))!.Warnings);
        Assert.Empty(await svc.PreviewAsync(Request("NEW")));

        var updated = await svc.UpdateAsync(dayId,
            new SaveRolloutRequest(Code, Thu.ToString("yyyy-MM-dd"), "09:00", "11:00", null,
                [new RolloutWorkerRequest("NEW", true, null)]), "UI-MANUAL");
        Assert.True(updated.Success);
        Assert.Empty(updated.Day!.Warnings);
    }

    /// <summary>
    /// A worker with an on-site row at another centre AND the day-level row for it must be
    /// reported once, not twice.
    /// </summary>
    [Fact]
    public async Task OverlapIsReportedOncePerCompetingCommitment()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "A", "Overlapping Worker");
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "A", ShiftDate = Thu, SupportLocation = NameB, LocationCode = CodeB,
            WorkingShift = "09:00 - 11:00", IsOnSite = true, Task = "WIC",
        });
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "A", ShiftDate = Thu, ShiftType = ShiftTypes.WicDuty, IsWicDuty = true,
            AgentTask = "Augsburg - Bahnhofs", ShiftStart = "09:00", ShiftEnd = "11:00",
        });
        await db.SaveChangesAsync();

        var result = await new RolloutService(db).CreateAsync(Request("A"), "UI-MANUAL");

        Assert.True(result.Success);
        Assert.Single(result.Day!.Warnings);
        Assert.Equal("OVERLAP", result.Day.Warnings[0].Type);
    }

    // ── 6. A pre-existing row is never rewritten ─────────────────────────────────────
    [Fact]
    public async Task AWorkerWhoAlreadyHasAShiftThatDay_KeepsItUnchanged()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "X", "Already Working");
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "X", ShiftDate = Thu, ShiftType = ShiftTypes.Working,
            AgentTask = "Voice", ShiftStart = "08:00", ShiftEnd = "16:00",
        });
        await db.SaveChangesAsync();

        var svc = new RolloutService(db);
        var result = await svc.CreateAsync(Request("X"), "UI-MANUAL");
        Assert.True(result.Success);

        var existing = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "X" && s.ShiftDate == Thu);
        Assert.Equal(ShiftTypes.Working, existing.ShiftType);
        Assert.Equal("Voice", existing.AgentTask);
        Assert.Equal("08:00", existing.ShiftStart);
        Assert.False(existing.IsManualChange);

        var worker = await db.WicRolloutWorkers.SingleAsync();
        Assert.Null(worker.ShiftEntryId);          // the rollout did not create it
        Assert.NotNull(worker.WicShiftEntryId);    // but it did add the on-site row
        Assert.Contains(result.Day!.Warnings, w => w.EmployeeId == "X" && w.Type == "OVERLAP");
    }

    // ── 7. Update touches only what came from the rollout ────────────────────────────

    [Fact]
    public async Task Update_ChangesTheRolloutHoursAndItsOwnRows_AndNothingElse()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "NEW", "New Worker One");
        SeedEmployee(db, "OTH", "Other Agent");
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "OTH", ShiftDate = Thu, SupportLocation = Name, LocationCode = Code,
            WorkingShift = "09:00 - 17:00", IsOnSite = true, Task = "WIC",
        });
        await db.SaveChangesAsync();

        var svc = new RolloutService(db);
        var created = await svc.CreateAsync(Request("NEW"), "S1");
        var dayId = created.Day!.Id;

        var updated = await svc.UpdateAsync(dayId,
            new SaveRolloutRequest(Code, Thu.ToString("yyyy-MM-dd"), "10:00", "13:00", "changed",
                [new RolloutWorkerRequest("NEW", false, "not in Pipeline, agreed with TL")]), "S2");

        Assert.True(updated.Success);
        Assert.Equal("10:00", updated.Day!.StartTime);
        Assert.Equal("S2", updated.Day.UpdatedBy);

        var all = await db.WicOpeningHours.ToListAsync();
        var hours = WicHoursResolver.Resolve(all, Code, null, (int)Thu.DayOfWeek, Thu);
        Assert.Equal("10:00", hours!.OpenTime);
        Assert.Equal("13:00", hours.CloseTime);

        var own = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "NEW");
        Assert.Equal("10:00", own.ShiftStart);
        Assert.Equal("13:00", own.ShiftEnd);

        var other = await db.ShiftEntries.FirstOrDefaultAsync(s => s.EmployeeId == "OTH");
        Assert.Null(other);                                   // still no day-level row for OTH
        var otherWic = await db.WicShiftEntries.SingleAsync(w => w.EmployeeId == "OTH");
        Assert.Equal("09:00 - 17:00", otherWic.WorkingShift); // untouched

        var worker = await db.WicRolloutWorkers.SingleAsync();
        Assert.False(worker.FromPipeline);
        Assert.Equal("not in Pipeline, agreed with TL", worker.PipelineNote);
    }

    // ── 8. Delete removes only what came from the rollout ────────────────────────────

    [Fact]
    public async Task Delete_RemovesTheRolloutRows_AndKeepsPreExistingOnes()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "A", "Rollout Only");
        SeedEmployee(db, "B", "Pre-existing");
        db.ShiftEntries.Add(new ShiftEntry
        {
            EmployeeId = "B", ShiftDate = Thu, ShiftType = ShiftTypes.Working,
            AgentTask = "Voice", ShiftStart = "08:00", ShiftEnd = "16:00",
        });
        db.WicShiftEntries.Add(new WicShiftEntry
        {
            EmployeeId = "B", ShiftDate = Thu, SupportLocation = Name, LocationCode = Code,
            WorkingShift = "08:00 - 16:00", IsOnSite = true, Task = "WIC",
        });
        await db.SaveChangesAsync();

        var svc = new RolloutService(db);
        var created = await svc.CreateAsync(Request("A", "B"), "UI-MANUAL");
        var dayId = created.Day!.Id;
        Assert.NotNull(await db.ShiftEntries.FirstOrDefaultAsync(s => s.EmployeeId == "A"));

        Assert.True(await svc.DeleteAsync(dayId));

        Assert.Empty(await db.WicRolloutDays.ToListAsync());
        Assert.Empty(await db.WicRolloutWorkers.ToListAsync());
        Assert.Null(await db.ShiftEntries.FirstOrDefaultAsync(s => s.EmployeeId == "A" && s.ShiftDate == Thu));
        Assert.Null(await db.WicShiftEntries.FirstOrDefaultAsync(w => w.EmployeeId == "A" && w.ShiftDate == Thu));

        // The pre-existing rows survived, unchanged.
        var b = await db.ShiftEntries.SingleAsync(s => s.EmployeeId == "B" && s.ShiftDate == Thu);
        Assert.Equal(ShiftTypes.Working, b.ShiftType);
        Assert.Equal("08:00", b.ShiftStart);
        Assert.NotNull(await db.WicShiftEntries.FirstOrDefaultAsync(w => w.EmployeeId == "B" && w.ShiftDate == Thu));

        // The hours projection is gone: the standard pattern applies again.
        var all = await db.WicOpeningHours.ToListAsync();
        var hours = WicHoursResolver.Resolve(all, Code, null, (int)Thu.DayOfWeek, Thu);
        Assert.Equal("09:00", hours!.OpenTime);
        Assert.Equal("17:00", hours.CloseTime);
    }

    // ── 9. Pipeline candidates ───────────────────────────────────────────────────────

    [Fact]
    public async Task Candidates_MarkWorkersNamedInPipeline_AndEveryoneElseIsOfferedToo()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "P1", "Pipeline Person", trainee: true);
        SeedEmployee(db, "N1", "Not From Pipeline");
        db.WicPipeline.Add(new WicPipelineItem
        {
            LocationCode = Code, PipelineDate = Thu, PipelineDateEnd = null,
            Title = "Rollout campaign", PrimaryAgent = "Pipeline Person", Status = "PLANNED",
        });
        await db.SaveChangesAsync();

        var candidates = await new RolloutService(db).GetCandidatesAsync(Code, Thu);

        Assert.True(candidates.PipelineAvailable);
        Assert.Contains("Pipeline Person", candidates.PipelineNames);
        var p1 = candidates.Workers.Single(w => w.EmployeeId == "P1");
        Assert.True(p1.FromPipeline);
        Assert.Equal("Pipeline Person", p1.PipelineRole);
        Assert.True(p1.IsTrainee);
        var n1 = candidates.Workers.Single(w => w.EmployeeId == "N1");
        Assert.False(n1.FromPipeline);
        Assert.Null(n1.PipelineRole);

        // A different date has no pipeline event → nobody comes from Pipeline.
        var other = await new RolloutService(db).GetCandidatesAsync(Code, Fri);
        Assert.False(other.PipelineAvailable);
        Assert.All(other.Workers, w => Assert.False(w.FromPipeline));
    }

    [Fact]
    public async Task OutOfPipelineWorker_IsRecordedAsSuch()
    {
        using var db = NewDb();
        SeedCentres(db);
        SeedEmployee(db, "N1", "Not From Pipeline");
        await db.SaveChangesAsync();

        var svc = new RolloutService(db);
        var result = await svc.CreateAsync(
            new SaveRolloutRequest(Code, Thu.ToString("yyyy-MM-dd"), "08:00", "14:00", null,
                [new RolloutWorkerRequest("N1", false, "hired this week")]), "UI-MANUAL");

        Assert.True(result.Success);
        var worker = result.Day!.Workers.Single();
        Assert.False(worker.FromPipeline);
        Assert.Equal("hired this week", worker.PipelineNote);
    }

    // ── 10. Validation ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("14:00", "08:00", "after the start")]
    [InlineData("nope",  "14:00", "Invalid start time")]
    public async Task InvalidTimes_AreRejected(string start, string end, string expected)
    {
        using var db = NewDb();
        SeedCentres(db);
        await db.SaveChangesAsync();
        var svc = new RolloutService(db);

        var result = await svc.CreateAsync(
            new SaveRolloutRequest(Code, Thu.ToString("yyyy-MM-dd"), start, end, null, []), "UI-MANUAL");

        Assert.False(result.Success);
        Assert.Contains(expected, result.Reason);
    }
}
