using Clube.Core.Application;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.Time.Testing;

namespace Clube.Core.Tests;

public class StatementPagingTests
{
    private const string Holder = "529.982.247-25";
    private static readonly DateTimeOffset Subscribed = DateTimeOffset.Parse("2026-10-20T15:00:00Z");

    private readonly FakeTimeProvider clock = new(Subscribed);
    private readonly IForManagingMemberships memberships;

    public StatementPagingTests() =>
        memberships = new MembershipManager(new InMemoryMembershipStore(), new FakeCaller(Actor.Anonymous, "corr-1"), clock);

    // One credit per month, so each call walks the clock forward to keep the period inside the window.
    private async Task<MembershipId> SubscribeWithCreditsAsync(int credits)
    {
        var id = new MembershipId(ResultAssert.Ok(await memberships.SubscribeAsync(Holder, default)).Id);
        for (int month = 0; month < credits; month++) await CreditMonthAsync(id, month);
        return id;
    }

    private async Task CreditMonthAsync(MembershipId id, int monthsAfterSubscription)
    {
        var moment = Subscribed.AddMonths(monthsAfterSubscription);
        clock.SetUtcNow(moment);
        ResultAssert.Ok(await memberships.CreditMonthlyAllowanceAsync(id, $"{moment:yyyy-MM}", default));
    }

    private async Task<StatementView> PageAsync(MembershipId id, int limit, StatementCursor? after = null) =>
        ResultAssert.Ok(await memberships.GetStatementAsync(id, ResultAssert.Ok(StatementPage.Create(limit, after)), default));

    [Fact]
    public async Task WalksEveryEntryExactlyOnceAcrossPages()
    {
        var id = await SubscribeWithCreditsAsync(5);
        var everything = await PageAsync(id, StatementPage.MaxLimit);

        var first = await PageAsync(id, 2);
        var second = await PageAsync(id, 2, first.Next);
        var third = await PageAsync(id, 2, second.Next);

        IEnumerable<Guid> walked = first.Lines.Concat(second.Lines).Concat(third.Lines).Select(line => line.TransactionId);
        Assert.Equal(everything.Lines.Select(line => line.TransactionId), walked);
        Assert.NotNull(first.Next);
        Assert.NotNull(second.Next);
        Assert.Null(third.Next);
        Assert.Null(everything.Next);
    }

    [Fact]
    public async Task EndsWithoutACursorWhenThePageFitsExactly()
    {
        var id = await SubscribeWithCreditsAsync(2);

        var page = await PageAsync(id, 2);

        Assert.Equal(2, page.Lines.Count);
        Assert.Null(page.Next);
    }

    [Fact]
    public async Task KeepsPagesStableWhenANewEntryArrivesBetweenThem()
    {
        var id = await SubscribeWithCreditsAsync(4);
        var first = await PageAsync(id, 2);

        await CreditMonthAsync(id, 4);
        var second = await PageAsync(id, 2, first.Next);

        var seen = first.Lines.Select(line => line.TransactionId).ToHashSet();
        Assert.Equal(2, second.Lines.Count);
        Assert.DoesNotContain(second.Lines, line => seen.Contains(line.TransactionId));
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task ReportsTheTotalBalanceNotThePageSum()
    {
        var id = await SubscribeWithCreditsAsync(3);

        var page = await PageAsync(id, 1);

        Assert.Equal(3 * Membership.MonthlyAllowance.Cents, page.BalanceCents);
        Assert.Equal(Membership.MonthlyAllowance.Cents, page.Lines.Sum(line => line.AmountCents));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(StatementPage.MaxLimit + 1)]
    public void RejectsLimitsOutsideTheAllowedRange(int limit)
    {
        var error = ResultAssert.Failed(StatementPage.Create(limit, null));

        Assert.Equal(MembershipErrors.InvalidLimit, error);
    }

    [Fact]
    public void DefaultsTheLimitWhenOmitted() =>
        Assert.Equal(StatementPage.DefaultLimit, ResultAssert.Ok(StatementPage.Create(null, null)).Limit);
}
