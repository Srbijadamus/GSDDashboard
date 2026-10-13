using System.Text.RegularExpressions;
using Xunit;

namespace GSDDashboard.Tests;

// REQUIREMENT 6 — the overlap guard in the two backfill tools:
//   Tools\Backfill-WicDutyCandidates.ps1
//   Tools\Backfill-WicFromManualShift.ps1
//
// Both scripts must never write a second IsOnSite row whose window overlaps an
// existing IsOnSite=1 WicShiftEntries row for the same EmployeeId+ShiftDate, must
// report the skipped row, and must never modify an existing row.
//
// Pester is not present in this repository and both scripts open a SQL connection
// before their first statement, so they cannot be executed by `dotnet test`
// (and the task forbids touching a database). The guard is therefore pinned two
// ways, as the task allows:
//   1. a focused SOURCE check that both scripts define the shared
//      `Test-WindowOverlap` helper with the exact server rule
//      (start1 < end2 AND start2 < end1), apply it BEFORE the executable INSERT,
//      mark skipped rows with OVERLAP_EXISTING_ONSITE, and contain no
//      UPDATE of WicShiftEntries, and
//   2. a comment-documented unit test of the EQUIVALENT logic in C#.
public class WicBackfillOverlapGuardTests
{
    private const string DutyCandidates = "Backfill-WicDutyCandidates.ps1";
    private const string FromManualShift = "Backfill-WicFromManualShift.ps1";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Tools", DutyCandidates))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Repo root with Tools\\{DutyCandidates} not found above {AppContext.BaseDirectory}");
    }

    private static string ReadTool(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Tools", fileName));

    public static IEnumerable<object[]> Scripts()
    {
        yield return new object[] { DutyCandidates };
        yield return new object[] { FromManualShift };
    }

    // (1) The shared helper exists and implements the exact server overlap rule.
    [Theory]
    [MemberData(nameof(Scripts))]
    public void BackfillTool_DefinesTheServerOverlapRule(string fileName)
    {
        var text = ReadTool(fileName);

        Assert.Contains("function Test-WindowOverlap", text);
        // start1 < end2 AND start2 < end1 — the same rule WicConflictDetector and
        // WicShiftService.CreateAssignmentAsync use.
        Assert.Matches(
            new Regex(@"return \(\$x0 -lt \$y1\) -and \(\$y0 -lt \$x1\)"),
            text);
    }

    // (2) A skipped row is reported with a stable marker and no row is written for it.
    [Theory]
    [MemberData(nameof(Scripts))]
    public void BackfillTool_ReportsOverlapSkips(string fileName)
    {
        Assert.Contains("OVERLAP_EXISTING_ONSITE", ReadTool(fileName));
    }

    // (3) The guard runs BEFORE the executable INSERT statement.
    [Theory]
    [MemberData(nameof(Scripts))]
    public void BackfillTool_TestsOverlapBeforeTheExecutableInsert(string fileName)
    {
        var text = ReadTool(fileName);
        var guard = fileName == DutyCandidates
            ? text.IndexOf("Test-OverlapsAny $planShift $occ", StringComparison.Ordinal)
            : text.IndexOf("Test-WindowOverlap $candidateWin", StringComparison.Ordinal);
        var insert = text.LastIndexOf("INSERT INTO WicShiftEntries", StringComparison.Ordinal);

        Assert.True(guard >= 0, $"{fileName}: overlap guard call not found");
        Assert.True(insert >= 0, $"{fileName}: executable INSERT not found");
        Assert.True(guard < insert, $"{fileName}: overlap guard must be evaluated before the INSERT");
    }

    // (4) Neither tool ever modifies an existing WicShiftEntries row (INSERT-only).
    [Theory]
    [MemberData(nameof(Scripts))]
    public void BackfillTool_NeverUpdatesAnExistingRow(string fileName)
    {
        var text = ReadTool(fileName);
        Assert.DoesNotContain("UPDATE WicShiftEntries", text);
    }

    [Fact]
    public void DutyCandidates_ApplyWritesOnlyTheGuardedPlanRows()
    {
        var text = ReadTool(DutyCandidates);
        // The Apply loop writes exactly $planWritable, which excludes every row the
        // guard cleared ($planShift = $null -> PlanSkip = OVERLAP_EXISTING_ONSITE).
        Assert.Contains("if (Test-OverlapsAny $planShift $occ)", text);
        Assert.Contains("$planSkip  = 'OVERLAP_EXISTING_ONSITE'", text);
    }

    [Fact]
    public void FromManualShift_ApplyInsertsOnly()
    {
        var text = ReadTool(FromManualShift);
        Assert.Contains("$toInsert = @($plan | Where-Object { $_.Action -eq \"INSERT\" })", text);
        Assert.Contains("APPLY REFUSED:", text); // HARD GATE 1 refuses any UPDATE plan
    }

    // (2) The equivalent logic, documented here because the .ps1 files cannot be
    // executed by the test runner: windows overlap when start1 < end2 AND
    // start2 < end1; touching endpoints and unknown times do not overlap.
    [Theory]
    [InlineData("10:00-14:00", "07:30-16:00", true)]  // contained
    [InlineData("10:00-14:00", "13:00-15:00", true)]  // partial
    [InlineData("10:00-14:00", "10:00-14:00", true)]  // identical
    [InlineData("10:00-12:00", "13:00-15:00", false)] // disjoint
    [InlineData("10:00-14:00", "14:00-16:00", false)] // touching at the endpoint
    [InlineData("", "10:00-14:00", false)]            // unknown time
    public void OverlapRule_MatchesTheServerRule(string a, string b, bool expected)
    {
        Assert.Equal(expected, WindowsOverlap(a, b));
    }

    // Mirrors Tools\*.ps1 `Test-WindowOverlap` and
    // WicConflictDetector.WindowsOverlap (start1 < end2 && start2 < end1).
    private static bool WindowsOverlap(string a, string b)
    {
        static (TimeSpan Start, TimeSpan End)? Window(string window)
        {
            var parts = window.Split('-');
            if (parts.Length != 2) return null;
            if (!TimeSpan.TryParse(parts[0], out var s) || !TimeSpan.TryParse(parts[1], out var e)) return null;
            return (s, e);
        }

        var w1 = Window(a);
        var w2 = Window(b);
        if (w1 is null || w2 is null) return false;
        return w1.Value.Start < w2.Value.End && w2.Value.Start < w1.Value.End;
    }
}
