namespace GSDDashboard.API.Data.Models;

// Single source of truth for how far ahead shifts/absences can be planned.
// AL is routinely requested many months in advance, so this must stay generous.
public static class ScheduleLimits
{
    public const int MaxFutureDays = 365;
}
