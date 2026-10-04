namespace GSDDashboard.API.Services;

/// <summary>
/// Test / non-operational employee accounts excluded from every operational view:
/// WIC Attendance (header + all lists), roster, cards, coverage, forecast and briefing.
///
/// RTM decision 2026-10-01. One named list on purpose — these ids must never be scattered
/// as literals across services, which is how such an exclusion silently applies in three
/// places and not the other four. The frontend holds the same list in
/// `Frontend/src/pages/attendance/checkinStatus.ts` (TEST_EMPLOYEE_IDS); keep them in sync.
/// </summary>
public static class TestAccounts
{
    /// <summary>
    /// List (not HashSet) so EF Core can translate <c>Contains</c> into a SQL IN clause.
    /// </summary>
    public static readonly List<string> ExcludedEmployeeIds = new()
    {
        "9074466", // DEMO / Stojnic Nebojsa — DEV account, not an operational agent
    };

    public static bool IsExcluded(string? employeeId) =>
        employeeId != null &&
        ExcludedEmployeeIds.Contains(employeeId.Trim(), StringComparer.OrdinalIgnoreCase);
}
