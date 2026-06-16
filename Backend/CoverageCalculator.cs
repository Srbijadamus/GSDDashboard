using GSDDashboard.API.Services;

namespace GSDDashboard.API.Modules.WicShifts;

public enum CoverageStatus { COVERED, PARTIAL, UNCOVERED, CLOSED }

public record CoverageBlock(string Open, string Close);

public record AgentCoverage(
    string EmployeeId,
    string Name,
    string? ShiftStart,
    string? ShiftEnd,
    bool IsMain,
    string CoverageMatch,
    int CoveredMinutes,
    int TotalOpenMinutes,
    string? MismatchNote
);

public record CoverageResult(
    CoverageStatus Status,
    int TotalOpenMinutes,
    int TotalCoveredMinutes,
    int CoveragePercent,
    List<AgentCoverage> Agents
);

public static class CoverageCalculator
{
    // Converts "09:30" to total minutes from midnight
    public static int ToMinutes(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return 0;
        var parts = time.Split(':');
        if (parts.Length != 2) return 0;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return 0;
        return h * 60 + m;
    }

    // Calculate overlap in minutes between two time blocks
    public static int Overlap(int startA, int endA, int startB, int endB)
    {
        var overlapStart = Math.Max(startA, startB);
        var overlapEnd   = Math.Min(endA, endB);
        return Math.Max(0, overlapEnd - overlapStart);
    }

    // Calculate total open minutes for a location (handles two blocks)
    public static int CalcOpenMinutes(string? open1, string? close1, string? open2, string? close2)
    {
        var mins = 0;
        if (!string.IsNullOrWhiteSpace(open1) && !string.IsNullOrWhiteSpace(close1))
            mins += Math.Max(0, ToMinutes(close1) - ToMinutes(open1));
        if (!string.IsNullOrWhiteSpace(open2) && !string.IsNullOrWhiteSpace(close2))
            mins += Math.Max(0, ToMinutes(close2) - ToMinutes(open2));
        return mins;
    }

    // Calculate how many minutes an agent covers across both blocks
    public static int CalcAgentCoverage(
        string? agentStart, string? agentEnd,
        string? open1, string? close1,
        string? open2, string? close2)
    {
        var aStart = ToMinutes(agentStart);
        var aEnd   = ToMinutes(agentEnd);
        if (aEnd == 0) return 0;

        var covered = 0;
        if (!string.IsNullOrWhiteSpace(open1) && !string.IsNullOrWhiteSpace(close1))
            covered += Overlap(aStart, aEnd, ToMinutes(open1), ToMinutes(close1));
        if (!string.IsNullOrWhiteSpace(open2) && !string.IsNullOrWhiteSpace(close2))
            covered += Overlap(aStart, aEnd, ToMinutes(open2), ToMinutes(close2));
        return covered;
    }

    public static CoverageResult Calculate(
        bool isClosed,
        string? open1, string? close1,
        string? open2, string? close2,
        List<(string EmployeeId, string Name, string? ShiftStart, string? ShiftEnd, bool IsMain)> agents)
    {
        if (isClosed)
            return new CoverageResult(CoverageEvaluator.ClassifyByMinutes(true, 0, 0), 0, 0, 0, []);

        var totalOpen = CalcOpenMinutes(open1, close1, open2, close2);
        if (totalOpen == 0)
            return new CoverageResult(CoverageEvaluator.ClassifyByMinutes(true, 0, 0), 0, 0, 0, []);

        var agentResults = agents.Select(a =>
        {
            var covered = CalcAgentCoverage(a.ShiftStart, a.ShiftEnd, open1, close1, open2, close2);
            var match = covered >= totalOpen ? "FULL"
                      : covered > 0         ? "PARTIAL"
                      : "NONE";

            string? note = null;
            if (match == "PARTIAL")
            {
                var aStart = ToMinutes(a.ShiftStart);
                var cStart = ToMinutes(open1);
                if (aStart > cStart)
                    note = $"Misses {open1}-{a.ShiftStart} ({aStart - cStart} min)";
            }

            return new AgentCoverage(a.EmployeeId, a.Name, a.ShiftStart, a.ShiftEnd,
                a.IsMain, match, covered, totalOpen, note);
        }).ToList();

        var totalCovered = agentResults.Sum(a => a.CoveredMinutes);
        // Pool: cap at totalOpen (multiple agents can cover same slot)
        var pooled = Math.Min(totalCovered, totalOpen);
        var pct = totalOpen > 0 ? (pooled * 100 / totalOpen) : 0;

        var status = CoverageEvaluator.ClassifyByMinutes(false, pooled, totalOpen);

        return new CoverageResult(status, totalOpen, pooled, pct, agentResults);
    }
}
