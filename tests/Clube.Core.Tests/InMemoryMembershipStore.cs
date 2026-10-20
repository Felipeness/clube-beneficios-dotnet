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
        Task.FromResult(Money.FromCents(EntriesOf(account).Sum(entry => entry.Line.AmountCents)));

    public Task<StatementSlice> StatementOfAsync(LedgerAccount account, StatementPage page, CancellationToken ct)
    {
        long before = page.After?.Position ?? long.MaxValue;
        var newestFirst = EntriesOf(account).Where(entry => entry.Position < before).Reverse().Take(page.Limit + 1).ToList();
        return Task.FromResult(StatementSlice.From(newestFirst, page));
    }

    public Task<string?> FindPostedFingerprintAsync(IdempotencyKey key, CancellationToken ct) =>
        Task.FromResult(postedTransactions.FirstOrDefault(transaction => transaction.Header.Key == key)?.Header.Fingerprint);

    public Task<PostedTransaction?> FindTransactionAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(ToPosted(postedTransactions.FirstOrDefault(transaction => transaction.Id == id)));

    private static PostedTransaction? ToPosted(LedgerTransaction? found) =>
        found is null ? null : new PostedTransaction(found.Id, found.Header.Description, found.Entries, found.Header.ReversesTransactionId);

    public Task<PostedTransaction?> FindTransactionByReferenceAsync(PaymentReference reference, CancellationToken ct) =>
        Task.FromResult(ToPosted(postedTransactions.FirstOrDefault(transaction => transaction.Header.ExternalReference == reference)));

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

    // Position counts every entry in the store, like the SQLite entry id, so it is stable as new postings arrive.
    private IEnumerable<PositionedLine> EntriesOf(LedgerAccount account) =>
        postedTransactions
            .SelectMany(transaction => transaction.Entries.Select(entry => (transaction, entry)))
            .Select((posted, index) => (posted.transaction, posted.entry, Position: index + 1L))
            .Where(posted => posted.entry.Account == account)
            .Select(posted => new PositionedLine(posted.Position, new StatementLine(posted.transaction.Id, posted.transaction.Header.Description, posted.entry.SignedCents, posted.transaction.Header.PostedAt, posted.transaction.Header.Caller.Actor.Kind, posted.transaction.Header.Caller.Actor.Id)));

    private bool HolderTaken() =>
        pendingMembers.Any(candidate => savedMembers.Values.Any(existing => existing.Holder == candidate.Holder));

    // Mirrors the unique indexes on the idempotency key and on the provider's payment reference.
    private bool KeyTaken() =>
        pendingTransactions.Any(candidate => postedTransactions.Any(posted =>
            posted.Header.Key == candidate.Header.Key
            || (candidate.Header.ExternalReference is not null && posted.Header.ExternalReference == candidate.Header.ExternalReference)));

    private void Commit()
    {
        pendingMembers.ForEach(membership => savedMembers[membership.Id] = membership);
        postedTransactions.AddRange(pendingTransactions);
    }
}
