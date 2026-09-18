using GSDDashboard.API.Modules.WicShifts;
using Xunit;

namespace GSDDashboard.Tests;

// Regression tests for Fix 2 (union of agent intervals instead of summed minutes).
// Background: Essenbach 2026-08 — two agents both 07:00-12:00 covered the same morning
// slot; the summed-minutes logic reported COVERED although nobody was on 12:30-13:30.
public class CoverageCalculatorUnionTests
{
    [Fact]
    public void TwoAgentsSameSlot_DoNotProduceFalseCovered()
    {
        // Essenbach pattern: open 07:00-12:00 + 12:30-16:00 (510 min),
        // both agents work 07:00-12:00 -> afternoon block is uncovered.
        var result = CoverageCalculator.Calculate(
            isClosed: false,
            open1: "07:00", close1: "12:00",
            open2: "12:30", close2: "16:00",
            agents:
            [
                ("E1", "Agent One", "07:00", "12:00", true),
                ("E2", "Agent Two", "07:00", "12:00", false),
            ]);

        Assert.Equal(CoverageStatus.PARTIAL, result.Status);
        Assert.Equal(300, result.TotalCoveredMinutes); // union 07:00-12:00 only
        Assert.Equal(510, result.TotalOpenMinutes);
        Assert.True(result.CoveragePercent < 100);
    }

    [Fact]
    public void OverlappingAgentsCoveringFullWindow_AreCovered()
    {
        // Agent A 07:00-13:00, Agent B 12:30-16:00 -> union covers the whole day.
        var result = CoverageCalculator.Calculate(
            isClosed: false,
            open1: "07:00", close1: "12:00",
            open2: "12:30", close2: "16:00",
            agents:
            [
                ("E1", "Agent One", "07:00", "13:00", true),
                ("E2", "Agent Two", "12:30", "16:00", false),
            ]);

        Assert.Equal(CoverageStatus.COVERED, result.Status);
        Assert.Equal(510, result.TotalCoveredMinutes);
        Assert.Equal(100, result.CoveragePercent);
    }

    [Fact]
    public void SingleAgentPartialWindow_StaysPartial()
    {
        var result = CoverageCalculator.Calculate(
            isClosed: false,
            open1: "08:30", close1: "16:30",
            open2: null, close2: null,
            agents:
            [
                ("E1", "Agent One", "07:00", "16:00", true),
            ]);

        Assert.Equal(CoverageStatus.PARTIAL, result.Status);
        Assert.Equal(450, result.TotalCoveredMinutes);
        Assert.Equal(480, result.TotalOpenMinutes);
    }
}
