using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Memberships;

public readonly record struct MembershipId(Guid Value)
{
    public static MembershipId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

// The balance is not stored here: it is the sum of this member's ledger entries.
public sealed class Membership
{
    public static readonly Money MonthlyAllowance = Money.FromCents(2000);

    public MembershipId Id { get; private set; }
    public Cpf Holder { get; private set; }
    public DateTimeOffset SubscribedAt { get; private set; }

    // Bumped on every posting to this member's account. As a concurrency token it serialises writers,
    // so two redemptions reading the same balance cannot both spend it.
    public int LedgerVersion { get; private set; }

    public LedgerAccount Account => LedgerAccount.Member(Id);

    // Parameter names match the properties so EF Core binds through this constructor.
    private Membership(MembershipId id, Cpf holder, DateTimeOffset subscribedAt)
    {
        Id = id;
        Holder = holder;
        SubscribedAt = subscribedAt;
    }

    public static Membership Subscribe(Cpf holder, DateTimeOffset now) => new(MembershipId.New(), holder, now);

    // What a request means, stored beside its key: the same key with another meaning is a client bug, not a retry.
    public static string RedemptionFingerprint(Reward reward) => $"redeem:{reward.Id}";

    // The allowance key already spells out member and period; what tells a redelivery from a second payment is the payment itself.
    public static string AllowanceFingerprint(PaymentReference reference) => $"payment:{reference}";

    // Only the exact allowance, for periods from the subscription month up to the current one: no advancing it.
    public Result<LedgerTransaction> CreditMonthlyAllowance(AllowancePayment payment, PostingContext context)
    {
        if (payment.Amount != MonthlyAllowance) return MembershipErrors.AllowanceAmountMismatch;
        if (payment.Period.Start > BillingPeriod.At(context.At).Start) return MembershipErrors.FuturePeriod;
        if (payment.Period.Start < BillingPeriod.At(SubscribedAt).Start) return MembershipErrors.PeriodBeforeSubscription;

        var header = new TransactionHeader(
            IdempotencyKey.ForAllowance(Id, payment.Period),
            AllowanceFingerprint(payment.Reference),
            $"Monthly allowance {payment.Period}",
            context.At,
            context.Caller,
            ExternalReference: payment.Reference);
        return PostTransfer(LedgerAccount.AllowanceFunding, Account, MonthlyAllowance, header);
    }

    public Result<LedgerTransaction> Redeem(Reward reward, Money balance, IdempotencyKey key, PostingContext context)
    {
        if (balance < reward.Price) return MembershipErrors.InsufficientBalance;

        return PostTransfer(Account, LedgerAccount.Redemptions, reward.Price, new TransactionHeader(key, RedemptionFingerprint(reward), $"Redemption {reward.Id}", context.At, context.Caller));
    }

    // Moves what CreditExpiry decided out of the member's account into breakage; nothing to expire is refused by the ledger.
    public Result<LedgerTransaction> ExpireCredits(Money expiring, BillingPeriod cutoff, PostingContext context)
    {
        var header = new TransactionHeader(
            IdempotencyKey.ForExpiry(Id, cutoff),
            $"expire:{expiring.Cents}",
            $"Credit expired (credits up to {cutoff})",
            context.At,
            context.Caller);
        return PostTransfer(Account, LedgerAccount.Breakage, expiring, header);
    }

    // The balance may end up negative (a credit reversed after being spent): that is a debt, and Redeem
    // keeps refusing new redemptions until the balance covers the price again.
    public Result<LedgerTransaction> Reverse(PostedTransaction original, PostingContext context) =>
        BumpVersionOnSuccess(LedgerTransaction.Reverse(original, context));

    private Result<LedgerTransaction> PostTransfer(LedgerAccount from, LedgerAccount to, Money amount, TransactionHeader header) =>
        BumpVersionOnSuccess(LedgerTransaction.Transfer(from, to, amount, header));

    private Result<LedgerTransaction> BumpVersionOnSuccess(Result<LedgerTransaction> posting)
    {
        if (posting.TryGetValue(out _, out _)) LedgerVersion++;
        return posting;
    }
}
