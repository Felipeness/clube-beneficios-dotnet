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

    public Result<LedgerTransaction> CreditMonthlyAllowance(BillingPeriod period, DateTimeOffset now) =>
        PostTransfer(
            LedgerAccount.AllowanceFunding,
            Account,
            MonthlyAllowance,
            new TransactionHeader(IdempotencyKey.ForAllowance(Id, period), $"Monthly allowance {period}", now));

    public Result<LedgerTransaction> Redeem(Reward reward, Money balance, IdempotencyKey key, DateTimeOffset now)
    {
        if (balance < reward.Price) return MembershipErrors.InsufficientBalance;

        return PostTransfer(Account, LedgerAccount.Redemptions, reward.Price, new TransactionHeader(key, $"Redemption {reward.Id}", now));
    }

    private Result<LedgerTransaction> PostTransfer(LedgerAccount from, LedgerAccount to, Money amount, TransactionHeader header)
    {
        Result<LedgerTransaction> transfer = LedgerTransaction.Transfer(from, to, amount, header);
        if (transfer.TryGetValue(out _, out _)) LedgerVersion++;
        return transfer;
    }
}
