using Clube.Core.Application;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Reconciliation;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.Time.Testing;

namespace Clube.Core.Tests;

public class ReconciliationTests
{
    private sealed class FakePsp : IForReadingPspSettlements
    {
        public List<PspSettlement> Settled { get; } = [];

        public Task<IReadOnlyList<PspSettlement>> SettlementsOnAsync(DateOnly day, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PspSettlement>>([.. Settled.Where(settlement => ClubeCalendar.DayOf(settlement.SettledAt) == day)]);
    }

    private static readonly DateOnly Day = new(2026, 10, 20);

    private readonly InMemoryMembershipStore store = new();
    private readonly FakePsp psp = new();
    private readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));

    private static PaymentReference Reference(string value) => ResultAssert.Ok(PaymentReference.Parse(value));

    private static PspSettlement Settlement(string reference, long cents, bool returned = false, string settledAt = "2026-10-20T14:59:00Z") =>
        new(Reference(reference), Money.FromCents(cents), DateTimeOffset.Parse(settledAt), returned);

    private static ReceivedPix Credit(string reference, long cents) => new(Reference(reference), Money.FromCents(cents));

    [Theory]
    [InlineData(2000, false, 2000, null)]
    [InlineData(2000, false, null, DivergenceKind.MissingInLedger)]
    [InlineData(null, false, 2000, DivergenceKind.MissingAtPsp)]
    [InlineData(2000, false, 1500, DivergenceKind.AmountMismatch)]
    [InlineData(2000, true, null, null)]                              // a duplicate payment sent back: consistent, nothing was owed
    [InlineData(2000, true, 2000, null)]                              // returned after being credited: the reversal is the ledger's side of it
    [InlineData(2000, true, 1500, DivergenceKind.AmountMismatch)]
    public void ClassifiesOnePixByWhatEachSideSaw(int? pspCents, bool returned, int? ledgerCents, DivergenceKind? expected)
    {
        IReadOnlyList<PspSettlement> settled = pspCents is int cents ? [Settlement("E1", cents, returned)] : [];
        IReadOnlyList<ReceivedPix> credited = ledgerCents is int ledger ? [Credit("E1", ledger)] : [];

        var divergences = PixReconciliation.Compare(settled, credited);

        Assert.Equal(expected is null ? [] : [expected.Value], divergences.Select(divergence => divergence.Kind));
    }

    [Fact]
    public void ReportsEveryKindTogetherInReferenceOrder()
    {
        var settled = new[] { Settlement("E3", 2000), Settlement("E2", 2000), Settlement("E1", 2000) };
        var credited = new[] { Credit("E1", 2000), Credit("E2", 500), Credit("E4", 2000) };

        var divergences = PixReconciliation.Compare(settled, credited);

        Assert.Equal(
            [("E2", DivergenceKind.AmountMismatch), ("E3", DivergenceKind.MissingInLedger), ("E4", DivergenceKind.MissingAtPsp)],
            divergences.Select(divergence => (divergence.EndToEndId.Value, divergence.Kind)));
    }

    private IForManagingMemberships NewHexagon() =>
        new MembershipManager(store, new FakeCaller(Actor.PixProvider, "corr-1"), clock);

    private async Task CreditAsync(string reference, string cpf = "529.982.247-25")
    {
        var memberships = NewHexagon();
        var id = new MembershipId(ResultAssert.Ok(await memberships.SubscribeAsync(cpf, default)).Id);
        ResultAssert.Ok(await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10", reference), default));
    }

    private Task<ReconciliationReport> ReconcileAsync(DateOnly day) => new ReconciliationRunner(store, psp).RunAsync(day, default);

    [Fact]
    public async Task ReportsNothingWhenBothSidesAgree()
    {
        await CreditAsync("E1");
        psp.Settled.Add(Settlement("E1", 2000));

        var report = await ReconcileAsync(Day);

        Assert.Empty(report.Divergences);
        Assert.Equal((1, 1), (report.SettledAtPsp, report.CreditedInLedger));
    }

    [Fact]
    public async Task FlagsMoneyReceivedThatWasNeverCredited()
    {
        psp.Settled.Add(Settlement("E1", 2000));

        var report = await ReconcileAsync(Day);

        Assert.Equal(1, report.MissingInLedger);
    }

    [Fact]
    public async Task KeepsADayInSaoPauloAcrossTheUtcMidnight()
    {
        // 23:30 on Oct 20 in Sao Paulo is already Oct 21 in UTC: the credit still belongs to the 20th.
        clock.SetUtcNow(DateTimeOffset.Parse("2026-10-21T02:30:00Z"));
        await CreditAsync("E1");

        Assert.Equal(1, (await ReconcileAsync(Day)).CreditedInLedger);
        Assert.Equal(0, (await ReconcileAsync(Day.AddDays(1))).CreditedInLedger);
    }

    [Fact]
    public async Task DoesNotFlagAPixSettledBeforeMidnightAndCreditedAfterIt()
    {
        clock.SetUtcNow(DateTimeOffset.Parse("2026-10-21T03:01:00Z"));
        await CreditAsync("E1");
        psp.Settled.Add(Settlement("E1", 2000, settledAt: "2026-10-21T02:59:00Z"));

        var report = await ReconcileAsync(Day);

        Assert.Empty(report.Divergences);
    }

    [Fact]
    public async Task PagesThroughADayLargerThanOnePage()
    {
        for (int i = 0; i < ReconciliationRunner.PageSize + 1; i++)
        {
            var memberships = NewHexagon();
            var id = new MembershipId(ResultAssert.Ok(await memberships.SubscribeAsync(CreditExpiryTests.CpfNumber(i), default)).Id);
            ResultAssert.Ok(await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10", $"E{i:D6}"), default));
        }

        Assert.Equal(ReconciliationRunner.PageSize + 1, (await ReconcileAsync(Day)).CreditedInLedger);
    }

    [Fact]
    public async Task CountsTheLedgerAsBalancedAfterNormalPostings()
    {
        await CreditAsync("E1");

        Assert.Equal(0, await new LedgerIntegrityChecker(store).ImbalanceCentsAsync(default));
    }
}
