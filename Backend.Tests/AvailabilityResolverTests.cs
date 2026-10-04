using GSDDashboard.API.Services;
using Xunit;

namespace GSDDashboard.Tests;

public class AvailabilityResolverTests
{
    // Same rule as RosterService.IsAvailable: no ShiftEntry at all is UNKNOWN, not a
    // verified "present" — an absence that was never imported can't be ruled out.
    [Fact]
    public void GetWicContribution_NoShiftEntry_ReturnsZero_NotPresent()
    {
        Assert.Equal(0.0, AvailabilityResolver.GetWicContribution(isSick: false, sh: null));
    }
}
