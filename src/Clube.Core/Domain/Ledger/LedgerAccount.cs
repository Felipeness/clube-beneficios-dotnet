using Clube.Core.Domain.Memberships;

namespace Clube.Core.Domain.Ledger;

public readonly record struct LedgerAccount
{
    // Where allowances come from and where redeemed value goes. Their balances read as the
    // club's liability and spend, which is what finance asks for at month end.
    public static readonly LedgerAccount AllowanceFunding = new("clube:allowance-funding");
    public static readonly LedgerAccount Redemptions = new("clube:redemptions");

    // Where expired credit goes. Its balance is breakage revenue already recognised: the club no longer owes it to anyone.
    public static readonly LedgerAccount Breakage = new("clube:breakage");

    private const string MemberPrefix = "member:";

    public string Value { get; }

    private LedgerAccount(string value) => Value = value;

    public static LedgerAccount Member(MembershipId id) => new($"{MemberPrefix}{id}");

    // The inverse of Member: which membership owns this account, or null for the club's own accounts.
    public MembershipId? Owner =>
        Value.StartsWith(MemberPrefix, StringComparison.Ordinal) && Guid.TryParse(Value.AsSpan(MemberPrefix.Length), out Guid id)
            ? new MembershipId(id)
            : null;

    // Rebuilds an account from what a store persisted; new accounts are only born through the factories above.
    public static LedgerAccount FromStored(string value) => new(value);

    public override string ToString() => Value;
}
