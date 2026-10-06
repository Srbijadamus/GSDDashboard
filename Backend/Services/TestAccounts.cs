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
    ///
    /// NEVER EMPTY. Four services evaluate <c>!ExcludedEmployeeIds.Contains(...)</c>
    /// inside an EF query (WicCardsService, BriefingService, ForecastService,
    /// WicCoverageGapService); EF Core rejects an empty collection as an inline query
    /// root ("Empty collections are not supported as inline query roots"), which made
    /// /api/wic/cards, /api/wic/briefing and /api/wic/forecast return HTTP 500 on
    /// 2026-10-06. The sentinel entry below can never equal a real EmployeeId, so this
    /// list excludes nobody while keeping the EF translation valid.
    ///
    /// 9074466 (Stojnic Nebojsa) was removed on 2026-10-06: he is a normal BO-group
    /// agent now and is counted like everyone else in Pulse and ShiftKiosk. The frontend
    /// list (TEST_EMPLOYEE_IDS) is empty because the browser has no such EF constraint.
    /// The list and <see cref="IsExcluded"/> stay in place for any future test account.
    /// </summary>
    public static readonly List<string> ExcludedEmployeeIds = new()
    {
        "__no_test_account__",
    };

    public static bool IsExcluded(string? employeeId) =>
        employeeId != null &&
        ExcludedEmployeeIds.Contains(employeeId.Trim(), StringComparer.OrdinalIgnoreCase);
}
