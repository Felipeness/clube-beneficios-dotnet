using Clube.Domain.Memberships;

namespace Clube.Domain.Tests;

public class BillingPeriodTests
{
    [Fact]
    public void ParsesYearAndMonth() =>
        Assert.Equal("2026-10", ResultAssert.Ok(BillingPeriod.Parse("2026-10")).ToString());

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026/10")]
    [InlineData("26-10")]
    [InlineData("2026-10-01")]
    [InlineData("")]
    public void RejectsMalformedPeriod(string raw) =>
        Assert.Equal(MembershipErrors.InvalidPeriod, ResultAssert.Failed(BillingPeriod.Parse(raw)));
}
