using Clube.Core.Application;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Vouchers;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.Time.Testing;

namespace Clube.Core.Tests;

public class OutboxTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-20T15:00:00Z");

    private readonly InMemoryMembershipStore store = new();
    private readonly FakeTimeProvider clock = new(Now);

    private IForManagingMemberships NewHexagon() =>
        new MembershipManager(store, new FakeCaller(Actor.Operator("operator-1"), "corr-1"), clock);

    private OutboxDispatcher NewDispatcher(FakeVoucherPartner partner) => new(store, partner, clock);

    private async Task<MembershipId> RedeemCinemaTicketAsync(IForManagingMemberships memberships)
    {
        var id = new MembershipId(ResultAssert.Ok(await memberships.SubscribeAsync("529.982.247-25", default)).Id);
        await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10"), default);
        ResultAssert.Ok(await memberships.RedeemRewardAsync(id, "cinema-ticket", "order-1", default));
        return id;
    }

    private async Task<IReadOnlyList<OutboxMessage>> SeedAsync(int count)
    {
        var seeded = Enumerable.Range(0, count)
            .Select(index => OutboxMessage.For(new RewardRedeemed(Guid.CreateVersion7(), MembershipId.New(), Now.AddSeconds(-count + index), "cinema-ticket", 1000)))
            .ToList();
        seeded.ForEach(store.Enqueue);
        await store.TrySaveChangesAsync(default);
        return seeded;
    }

    private OutboxMessage Stored(OutboxMessage message) => store.Outbox.Single(stored => stored.Id == message.Id);

    [Fact]
    public async Task RedemptionEnqueuesRewardRedeemedForThePostedTransaction()
    {
        var memberships = NewHexagon();
        var id = await RedeemCinemaTicketAsync(memberships);

        var message = Assert.Single(store.Outbox);
        var redeemed = Assert.IsType<RewardRedeemed>(message.Event);
        var redemption = store.PostedTransactions.Single(transaction => transaction.Header.Description == "Redemption cinema-ticket");
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(new RewardRedeemed(redemption.Id, id, Now, "cinema-ticket", 1000), redeemed);
    }

    [Fact]
    public async Task RetriedOrRefusedRedemptionsEnqueueNothingMore()
    {
        var memberships = NewHexagon();
        var id = await RedeemCinemaTicketAsync(memberships);

        await memberships.RedeemRewardAsync(id, "cinema-ticket", "order-1", default);
        ResultAssert.Failed(await memberships.RedeemRewardAsync(id, "perfume-kit", "order-2", default));

        Assert.Single(store.Outbox);
    }

    [Fact]
    public async Task RejectedSaveDropsTheMessageWithThePosting()
    {
        var memberships = NewHexagon();
        await RedeemCinemaTicketAsync(memberships);
        var alreadyPosted = store.PostedTransactions[^1];

        store.Append(alreadyPosted);
        store.Enqueue(OutboxMessage.For(new RewardRedeemed(alreadyPosted.Id, MembershipId.New(), Now, "cinema-ticket", 1000)));
        bool saved = await store.TrySaveChangesAsync(default);

        Assert.False(saved);
        Assert.Single(store.Outbox);
    }

    [Fact]
    public async Task ReversingARedemptionEnqueuesItsVoucherCancellation()
    {
        var memberships = NewHexagon();
        var id = await RedeemCinemaTicketAsync(memberships);
        var redemption = store.PostedTransactions.Single(transaction => transaction.Header.Description == "Redemption cinema-ticket");
        var credit = store.PostedTransactions.Single(transaction => transaction.Header.Description == "Monthly allowance 2026-10");

        await memberships.ReverseTransactionAsync(id, redemption.Id, default);
        await memberships.ReverseTransactionAsync(id, credit.Id, default);
        var partner = new FakeVoucherPartner();
        await NewDispatcher(partner).DispatchBatchAsync(default);

        var cancellation = Assert.Single(store.Outbox, message => message.Event is RedemptionReversed);
        Assert.Equal(redemption.Id, Assert.IsType<RedemptionReversed>(cancellation.Event).RedemptionTransactionId);
        Assert.Equal(2, store.Outbox.Count);
        Assert.Equal([redemption.Id], partner.Cancelled);
    }

    [Fact]
    public async Task DeliversDueMessagesKeyedByTheirTransaction()
    {
        var memberships = NewHexagon();
        var id = await RedeemCinemaTicketAsync(memberships);
        var partner = new FakeVoucherPartner();

        var settled = await NewDispatcher(partner).DispatchBatchAsync(default);

        var delivered = Assert.Single(settled);
        var redeemed = Assert.IsType<RewardRedeemed>(delivered.Event);
        Assert.Equal(OutboxStatus.Delivered, Stored(delivered).Status);
        Assert.Equal(1, delivered.Attempts);
        Assert.Equal([new VoucherRequest(redeemed.TransactionId, id, "cinema-ticket")], partner.Issued);
    }

    [Fact]
    public async Task NeverRedeliversADeliveredMessage()
    {
        await SeedAsync(1);
        var partner = new FakeVoucherPartner();
        var dispatcher = NewDispatcher(partner);

        await dispatcher.DispatchBatchAsync(default);
        clock.Advance(TimeSpan.FromDays(1));
        var second = await dispatcher.DispatchBatchAsync(default);

        Assert.Empty(second);
        Assert.Equal(1, partner.Calls);
    }

    [Fact]
    public async Task BacksOffExponentiallyWithJitterWhileThePartnerFails()
    {
        var message = (await SeedAsync(1))[0];
        var partner = new FakeVoucherPartner(failuresBeforeSuccess: int.MaxValue);
        var dispatcher = NewDispatcher(partner);

        for (int failedAttempts = 1; failedAttempts <= 3; failedAttempts++)
        {
            await dispatcher.DispatchBatchAsync(default);
            var retrying = Stored(message);
            var earliest = clock.GetUtcNow() + (OutboxDispatcher.FirstRetryDelay * Math.Pow(2, failedAttempts - 1));
            var latest = clock.GetUtcNow() + (OutboxDispatcher.FirstRetryDelay * Math.Pow(2, failedAttempts - 1) * (1 + OutboxDispatcher.MaxJitter));

            Assert.Equal((OutboxStatus.Pending, failedAttempts), (retrying.Status, retrying.Attempts));
            Assert.InRange(retrying.NextAttemptAt, earliest, latest);
            Assert.Contains("503", retrying.LastError);
            clock.SetUtcNow(retrying.NextAttemptAt);
        }
    }

    [Fact]
    public async Task WaitsForTheBackoffBeforeRetrying()
    {
        var message = (await SeedAsync(1))[0];
        var partner = new FakeVoucherPartner(failuresBeforeSuccess: 1);
        var dispatcher = NewDispatcher(partner);

        await dispatcher.DispatchBatchAsync(default);
        clock.Advance(OutboxDispatcher.FirstRetryDelay - TimeSpan.FromSeconds(1));
        var tooEarly = await dispatcher.DispatchBatchAsync(default);
        clock.SetUtcNow(Stored(message).NextAttemptAt);
        var retried = await dispatcher.DispatchBatchAsync(default);

        Assert.Empty(tooEarly);
        Assert.Equal((OutboxStatus.Delivered, 2), (Assert.Single(retried).Status, Stored(message).Attempts));
    }

    [Fact]
    public async Task DeadLettersAfterTheLastAttempt()
    {
        var message = (await SeedAsync(1))[0];
        var partner = new FakeVoucherPartner(failuresBeforeSuccess: int.MaxValue);
        var dispatcher = NewDispatcher(partner);

        for (int attempt = 1; attempt <= OutboxDispatcher.MaxAttempts; attempt++)
        {
            await dispatcher.DispatchBatchAsync(default);
            clock.SetUtcNow(Stored(message).NextAttemptAt);
        }
        clock.Advance(TimeSpan.FromDays(30));
        var afterDeadLetter = await dispatcher.DispatchBatchAsync(default);

        Assert.Equal((OutboxStatus.DeadLettered, OutboxDispatcher.MaxAttempts), (Stored(message).Status, Stored(message).Attempts));
        Assert.Empty(afterDeadLetter);
        Assert.Equal(OutboxDispatcher.MaxAttempts, partner.Calls);
    }

    [Fact]
    public async Task GivesUpOnAPartnerThatDoesNotAnswerInTime()
    {
        var message = (await SeedAsync(1))[0];
        var dispatching = NewDispatcher(new FakeVoucherPartner(hangs: true)).DispatchBatchAsync(default);

        clock.Advance(OutboxDispatcher.PartnerTimeout);
        await dispatching;

        Assert.Equal((OutboxStatus.Pending, 1), (Stored(message).Status, Stored(message).Attempts));
        Assert.Contains("did not answer", Stored(message).LastError);
    }

    [Fact]
    public async Task KeepsDeliveringTheRestWhenOneMessageFails()
    {
        var seeded = await SeedAsync(2);

        await NewDispatcher(new FakeVoucherPartner(failuresBeforeSuccess: 1)).DispatchBatchAsync(default);

        Assert.Equal([OutboxStatus.Pending, OutboxStatus.Delivered], seeded.Select(message => Stored(message).Status));
    }

    [Fact]
    public async Task SettlesAtMostOneBatchPerCall()
    {
        await SeedAsync(OutboxDispatcher.BatchSize + 5);

        var settled = await NewDispatcher(new FakeVoucherPartner()).DispatchBatchAsync(default);

        Assert.Equal(OutboxDispatcher.BatchSize, settled.Count);
    }
}
