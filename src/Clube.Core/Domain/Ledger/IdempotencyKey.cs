using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Ledger;

public readonly record struct IdempotencyKey
{
    private const int MaxClientKeyLength = 64;

    public string Value { get; }

    private IdempotencyKey(string value) => Value = value;

    // One allowance per member per period: a retried charge maps to the same key and is refused by the store.
    public static IdempotencyKey ForAllowance(MembershipId id, BillingPeriod period) => new($"allowance:{id}:{period}");

    // Scoped per membership so two members can never collide on the same client-generated key.
    public static Result<IdempotencyKey> ForRedemption(MembershipId id, string clientKey) =>
        string.IsNullOrWhiteSpace(clientKey) || clientKey.Length > MaxClientKeyLength
            ? LedgerErrors.InvalidIdempotencyKey
            : new IdempotencyKey($"redemption:{id}:{clientKey}");

    public override string ToString() => Value;
}
