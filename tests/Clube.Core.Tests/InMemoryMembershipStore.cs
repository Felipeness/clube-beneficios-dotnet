using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driven;

namespace Clube.Core.Tests;

// Second driven adapter for the same port: lets the hexagon run with no database at all.
internal sealed class InMemoryMembershipStore : IForStoringMemberships
{
    private readonly Dictionary<MembershipId, Membership> savedMembers = [];
    private readonly List<LedgerTransaction> postedTransactions = [];
    private readonly List<Membership> pendingMembers = [];
    private readonly List<LedgerTransaction> pendingTransactions = [];

    public IReadOnlyList<LedgerTransaction> PostedTransactions => postedTransactions;

    public Task<Membership?> FindAsync(MembershipId id, CancellationToken ct) =>
        Task.FromResult(savedMembers.GetValueOrDefault(id));

    public Task<Money> BalanceOfAsync(LedgerAccount account, CancellationToken ct) =>
        Task.FromResult(Money.FromCents(EntriesOf(account).Sum(line => line.AmountCents)));

    public Task<IReadOnlyList<StatementLine>> StatementOfAsync(LedgerAccount account, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StatementLine>>([.. EntriesOf(account).Reverse().Take(limit)]);

    public Task<string?> FindPostedFingerprintAsync(IdempotencyKey key, CancellationToken ct) =>
        Task.FromResult(postedTransactions.FirstOrDefault(transaction => transaction.Header.Key == key)?.Header.Fingerprint);

    public void Add(Membership membership) => pendingMembers.Add(membership);

    public void Append(LedgerTransaction transaction) => pendingTransactions.Add(transaction);

    public Task<bool> TrySaveChangesAsync(CancellationToken ct)
    {
        bool rejected = HolderTaken() || KeyTaken();
        if (!rejected) Commit();

        pendingMembers.Clear();
        pendingTransactions.Clear();
        return Task.FromResult(!rejected);
    }

    private IEnumerable<StatementLine> EntriesOf(LedgerAccount account) =>
        postedTransactions.SelectMany(transaction => transaction.Entries
            .Where(entry => entry.Account == account)
            .Select(entry => new StatementLine(transaction.Id, transaction.Header.Description, entry.SignedCents, transaction.Header.PostedAt, transaction.Header.Caller.Actor.Kind, transaction.Header.Caller.Actor.Id)));

    private bool HolderTaken() =>
        pendingMembers.Any(candidate => savedMembers.Values.Any(existing => existing.Holder == candidate.Holder));

    private bool KeyTaken() =>
        pendingTransactions.Any(candidate => postedTransactions.Any(posted => posted.Header.Key == candidate.Header.Key));

    private void Commit()
    {
        pendingMembers.ForEach(membership => savedMembers[membership.Id] = membership);
        postedTransactions.AddRange(pendingTransactions);
    }
}
