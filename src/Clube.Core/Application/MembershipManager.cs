using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;

namespace Clube.Core.Application;

// Implements the driving port by orchestrating the domain and calling out through driven ports.
// TimeProvider is a driven port too; the BCL already ships it as an abstraction with a fake for tests.
public sealed class MembershipManager(IForStoringMemberships memberships, IForIdentifyingTheCaller caller, TimeProvider clock) : IForManagingMemberships
{
    public async Task<Result<MembershipView>> SubscribeAsync(string cpf, CancellationToken ct)
    {
        if (!Cpf.Parse(cpf).TryGetValue(out var holder, out var invalidCpf)) return invalidCpf;

        var membership = Membership.Subscribe(holder, clock.GetUtcNow());
        memberships.Add(membership);

        // The store's uniqueness rule is the source of truth; a pre-check would still race.
        if (!await memberships.TrySaveChangesAsync(ct)) return MembershipErrors.AlreadySubscribed;
        return MembershipView.From(membership, Money.Zero);
    }

    public async Task<Result<MembershipView>> GetAsync(MembershipId id, CancellationToken ct)
    {
        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;

        return await ViewOfAsync(membership, ct);
    }

    public async Task<Result<StatementView>> GetStatementAsync(MembershipId id, StatementPage page, CancellationToken ct)
    {
        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;

        var balance = await memberships.BalanceOfAsync(membership.Account, ct);
        var slice = await memberships.StatementOfAsync(membership.Account, page, ct);
        return new StatementView(id.Value, balance.Cents, slice.Lines, slice.Next);
    }

    public async Task<Result<AllowanceCreditOutcome>> CreditMonthlyAllowanceAsync(MembershipId id, ReceivedPayment payment, CancellationToken ct)
    {
        if (!ParsePayment(payment).TryGetValue(out var allowance, out var invalidPayment)) return invalidPayment;

        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        // Fast path for redeliveries; the unique index on the key is what actually guarantees it. A paid period never
        // reaches the window check again, and another payment for it is the member paying twice, not a retry.
        string? postedFingerprint = await memberships.FindPostedFingerprintAsync(IdempotencyKey.ForAllowance(id, allowance.Period), ct);
        if (postedFingerprint is not null)
            return postedFingerprint == Membership.AllowanceFingerprint(allowance.Reference) ? AllowanceCreditOutcome.AlreadyCredited : AllowanceCreditOutcome.DuplicatePayment;
        if (!membership.CreditMonthlyAllowance(allowance, PostingContextNow()).TryGetValue(out var posting, out var rejected)) return rejected;

        if (!await TryPostAsync(posting, ct)) return MembershipErrors.ConcurrentUpdate;
        return AllowanceCreditOutcome.Credited;
    }

    public async Task<Result<PaymentRefundOutcome>> RefundPaymentAsync(string originalReference, CancellationToken ct)
    {
        if (!caller.Current.Actor.MayReverseTransactions) return LedgerErrors.ReversalForbidden;
        if (!PaymentReference.Parse(originalReference).TryGetValue(out var reference, out var invalidReference)) return invalidReference;

        var credit = await memberships.FindTransactionByReferenceAsync(reference, ct);
        if (credit?.Member is not MembershipId owner) return PaymentRefundOutcome.UnknownPayment;
        if (await memberships.FindPostedFingerprintAsync(IdempotencyKey.ForReversal(credit.Id), ct) is not null) return PaymentRefundOutcome.AlreadyRefunded;

        var reversed = await ReverseTransactionAsync(owner, credit.Id, ct);
        return reversed.TryGetValue(out _, out var rejected) ? PaymentRefundOutcome.Refunded : rejected;
    }

    private static Result<AllowancePayment> ParsePayment(ReceivedPayment payment)
    {
        if (!PaymentReference.Parse(payment.Reference).TryGetValue(out var reference, out var invalidReference)) return invalidReference;
        if (!BillingPeriod.Parse(payment.Period).TryGetValue(out var period, out var invalidPeriod)) return invalidPeriod;

        return new AllowancePayment(period, Money.FromCents(payment.AmountCents), reference);
    }

    public async Task<Result<MembershipView>> RedeemRewardAsync(MembershipId id, string rewardId, string idempotencyKey, CancellationToken ct)
    {
        if (!RewardCatalog.Find(rewardId).TryGetValue(out var reward, out var unknownReward)) return unknownReward;
        if (!IdempotencyKey.ForRedemption(id, idempotencyKey).TryGetValue(out var key, out var invalidKey)) return invalidKey;

        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        // A retried request answers with the current state and charges nothing; the same key for another reward is refused.
        string? postedFingerprint = await memberships.FindPostedFingerprintAsync(key, ct);
        if (postedFingerprint is not null)
            return postedFingerprint == Membership.RedemptionFingerprint(reward) ? await ViewOfAsync(membership, ct) : LedgerErrors.IdempotencyKeyReused;

        var balance = await memberships.BalanceOfAsync(membership.Account, ct);
        if (!membership.Redeem(reward, balance, key, PostingContextNow()).TryGetValue(out var posting, out var rejected)) return rejected;

        return await PostAsync(membership, posting, ct);
    }

    public async Task<Result<MembershipView>> ReverseTransactionAsync(MembershipId id, Guid transactionId, CancellationToken ct)
    {
        // Defence in depth: the HTTP policy already demands an operator, but a driving adapter that forgets it must not open the door.
        if (!caller.Current.Actor.MayReverseTransactions) return LedgerErrors.ReversalForbidden;
        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        // Another member's transaction looks the same as a missing one: nothing to learn from the difference.
        var original = await memberships.FindTransactionAsync(transactionId, ct);
        if (original is null || !original.Touches(membership.Account)) return MembershipErrors.TransactionNotFound;
        // A retried reversal answers with the current state; the unique key is what guarantees it under a race.
        if (await memberships.FindPostedFingerprintAsync(IdempotencyKey.ForReversal(transactionId), ct) is not null) return await ViewOfAsync(membership, ct);
        if (!membership.Reverse(original, PostingContextNow()).TryGetValue(out var posting, out var rejected)) return rejected;

        return await PostAsync(membership, posting, ct);
    }

    private PostingContext PostingContextNow() => new(clock.GetUtcNow(), caller.Current);

    private async Task<Result<MembershipView>> PostAsync(Membership membership, LedgerTransaction posting, CancellationToken ct)
    {
        if (!await TryPostAsync(posting, ct)) return MembershipErrors.ConcurrentUpdate;

        return await ViewOfAsync(membership, ct);
    }

    private Task<bool> TryPostAsync(LedgerTransaction posting, CancellationToken ct)
    {
        memberships.Append(posting);
        return memberships.TrySaveChangesAsync(ct);
    }

    private async Task<MembershipView> ViewOfAsync(Membership membership, CancellationToken ct) =>
        MembershipView.From(membership, await memberships.BalanceOfAsync(membership.Account, ct));
}
