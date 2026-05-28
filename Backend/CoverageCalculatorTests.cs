namespace GSDDashboard.API.Modules.WicShifts;

public static class CoverageCalculatorTests
{
    public static void RunAll()
    {
        Test1_Mulheim_Partial();
        Test2_Salzgitter_Covered();
        Test3_Landshut_Closed();
        Console.WriteLine("All 3 coverage tests PASSED.");
    }

    static void Test1_Mulheim_Partial()
    {
        // Mulheim Thu: 08:00-12:00 (240 min), agent 09:00-17:00
        // Overlap: 09:00-12:00 = 180 min → PARTIAL
        var result = CoverageCalculator.Calculate(
            isClosed: false,
            open1: "08:00", close1: "12:00",
            open2: null, close2: null,
            agents: [("EMP1", "Aman Kedo", "09:00", "17:00", true)]
        );
        Assert(result.Status == CoverageStatus.PARTIAL, "Test1: Expected PARTIAL");
        Assert(result.TotalOpenMinutes == 240, $"Test1: Expected 240 open min, got {result.TotalOpenMinutes}");
        Assert(result.TotalCoveredMinutes == 180, $"Test1: Expected 180 covered min, got {result.TotalCoveredMinutes}");
        Console.WriteLine("Test1 PASSED: Mulheim PARTIAL (180/240 min)");
    }

    static void Test2_Salzgitter_Covered()
    {
        // Salzgitter Mon: 09:00-17:00 (480 min), agent 08:00-17:00
        // Overlap: 09:00-17:00 = 480 min → COVERED
        var result = CoverageCalculator.Calculate(
            isClosed: false,
            open1: "09:00", close1: "17:00",
            open2: null, close2: null,
            agents: [("EMP2", "Ahmad Dabbas", "08:00", "17:00", true)]
        );
        Assert(result.Status == CoverageStatus.COVERED, "Test2: Expected COVERED");
        Assert(result.TotalOpenMinutes == 480, $"Test2: Expected 480 open min, got {result.TotalOpenMinutes}");
        Assert(result.TotalCoveredMinutes == 480, $"Test2: Expected 480 covered min, got {result.TotalCoveredMinutes}");
        Console.WriteLine("Test2 PASSED: Salzgitter COVERED (480/480 min)");
    }

    static void Test3_Landshut_Closed()
    {
        // Landshut Mon: Closed → CLOSED, no calculation
        var result = CoverageCalculator.Calculate(
            isClosed: true,
            open1: null, close1: null,
            open2: null, close2: null,
            agents: [("EMP3", "Khaled Alali", "08:00", "17:00", true)]
        );
        Assert(result.Status == CoverageStatus.CLOSED, "Test3: Expected CLOSED");
        Assert(result.TotalOpenMinutes == 0, $"Test3: Expected 0 open min, got {result.TotalOpenMinutes}");
        Console.WriteLine("Test3 PASSED: Landshut CLOSED");
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"FAILED: {message}");
    }
}
