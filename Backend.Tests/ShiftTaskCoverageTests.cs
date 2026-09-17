using System.Linq;
using GSDDashboard.API.Data;
using GSDDashboard.API.Data.Models;
using GSDDashboard.API.Modules.Shifts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GSDDashboard.Tests;

// Regression tests for the Shift Plan "Task" default rule and the /api/shifts/coverage
// per-hour aggregation. Background: a colleague reported that Kevin Heynen (Dispatcher)
// was shown with Task = Voice, and that Coverage per Hour suggested ~44 Voice agents at
// 09:00 — both caused by treating every WORKING shift as "Voice" regardless of role/task.
// See documentation/BLUEPRINT_LOGIC.md "Coverage per hour" and PROJECT_BLUEPRINT.md.
public class ShiftTaskCoverageTests
{
    private static GSDContext NewDb()
    {
        var options = new DbContextOptionsBuilder<GSDContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GSDContext(options);
    }

    [Fact]
    public void DefaultTask_NonVoiceRole_DoesNotDefaultToVoice()
    {
        // Kevin Heynen: Role = Dispatcher, no AgentTask saved yet.
        var effective = AgentTasks.Resolve(null, "Dispatcher");
        Assert.Equal("Dispatcher", effective);
        Assert.NotEqual("Voice", effective);
    }

    [Fact]
    public void DefaultTask_VoiceRole_DefaultsToVoice()
    {
        Assert.Equal("Voice", AgentTasks.Resolve(null, "Voice"));
    }

    [Fact]
    public void DefaultTask_RoleWithoutMatchingTask_HasNoDefault()
    {
        // Chat/Chat CRO/Trainer/Booking Tool/Bulk PWs have no dedicated Task option.
        Assert.Null(AgentTasks.Resolve(null, "Chat"));
        Assert.Null(AgentTasks.Resolve(null, "Trainer"));
    }

    [Fact]
    public void DefaultTask_ManuallySavedTask_IsNeverOverridden()
    {
        // A Dispatcher manually reassigned to Backlog must stay Backlog, not revert to "Dispatcher".
        Assert.Equal("Backlog", AgentTasks.Resolve("Backlog", "Dispatcher"));
    }

    [Fact]
    public void IsValid_AcceptsAllDocumentedTasksAndNull()
    {
        Assert.True(AgentTasks.IsValid(null));
        foreach (var task in AgentTasks.All)
            Assert.True(AgentTasks.IsValid(task));
        Assert.False(AgentTasks.IsValid("NotARealTask"));
    }

    [Fact]
    public async Task GetCoverageAsync_OnlyCountsExplicitVoiceTaskAsVoice()
    {
        using var db = NewDb();
        var date = new DateOnly(2026, 9, 16);

        // Voice role, no task saved -> defaults to Voice -> counts.
        db.Employees.Add(new Employee { EmployeeId = "1", FullName = "Voice Agent", PrimaryRole = "Voice", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "1", ShiftDate = date, ShiftType = "WORKING", ShiftStart = "08:00", ShiftEnd = "16:00" });

        // Dispatcher role, no task saved -> defaults to Dispatcher -> must NOT count as Voice.
        db.Employees.Add(new Employee { EmployeeId = "2", FullName = "Kevin Heynen", PrimaryRole = "Dispatcher", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "2", ShiftDate = date, ShiftType = "WORKING", ShiftStart = "08:00", ShiftEnd = "16:00" });

        // SME role explicitly reassigned to Backlog -> counts as Backlog, not Voice.
        db.Employees.Add(new Employee { EmployeeId = "3", FullName = "SME Agent", PrimaryRole = "SME", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "3", ShiftDate = date, ShiftType = "WORKING", ShiftStart = "08:00", ShiftEnd = "16:00", AgentTask = "Backlog" });

        // VWIC role -> counts as VWIC, not Voice.
        db.Employees.Add(new Employee { EmployeeId = "4", FullName = "VWIC Agent", PrimaryRole = "VWIC", IsActive = true });
        db.ShiftEntries.Add(new ShiftEntry { EmployeeId = "4", ShiftDate = date, ShiftType = "WORKING", ShiftStart = "08:00", ShiftEnd = "16:00" });

        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.GetCoverageAsync(date);
        var slot = result.Slots.Single(s => s.Hour == "09:00");

        Assert.Equal(1, slot.Voice);
        Assert.Equal(1, slot.Vwic);
        Assert.Equal(1, slot.Backlog);
        Assert.Equal(1, slot.Other); // Dispatcher
    }
}
