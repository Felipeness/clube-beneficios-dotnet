using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;

namespace Clube.Core.Application;

// Walks every member in keyset pages and posts what CreditExpiry says has expired. Each member is its own unit of work,
// so one failure is reported and retried by the next run without holding back or rolling back anyone else.
public sealed class CreditExpiryRunner(IForStoringMemberships memberships, TimeProvider clock) : IForExpiringCredits
{
    public const int PageSize = 100;

    private sealed record ExpiryRun(BillingPeriod Cutoff, CallerContext Caller);

    public async Task<CreditExpiryReport> RunAsync(BillingPeriod cutoff, CancellationToken ct)
    {
        var run = new ExpiryRun(cutoff, new CallerContext(Actor.CreditExpiry, $"credit-expiry:{Guid.CreateVersion7():N}"));
        var tally = new RunTally();
        MembershipId? after = null;
        IReadOnlyList<MembershipId> page;
        do
        {
            page = await memberships.MembershipIdsAfterAsync(after, PageSize, ct);
            foreach (MembershipId id in page) tally.Record(id, await ExpireSafelyAsync(id, run, ct));
            after = page.Count > 0 ? page[^1] : after;
        }
        while (page.Count == PageSize);

        return tally.ReportFor(cutoff);
    }

    private async Task<Result<Money>> ExpireSafelyAsync(MembershipId id, ExpiryRun run, CancellationToken ct)
    {
        try
        {
            return await ExpireAsync(id, run, ct);
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            // A bug or a database hiccup on one member is that member's problem; Conflict because the next run retries it.
            return new Error("credit_expiry.failed", $"{failure.GetType().Name}: {failure.Message}", ErrorKind.Conflict);
        }
    }

    private async Task<Result<Money>> ExpireAsync(MembershipId id, ExpiryRun run, CancellationToken ct)
    {
        if (await memberships.FindPostedFingerprintAsync(IdempotencyKey.ForExpiry(id, run.Cutoff), ct) is not null) return Money.Zero;
        // Cheap filter first, so members with nothing to expire are never loaded (and never tracked) at all.
        if (!(await ExpiringAsync(id, run.Cutoff, ct)).IsPositive) return Money.Zero;

        // Loaded before the totals that set the amount: any posting committed after this load bumps the version and fails the save.
        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        Money expiring = await ExpiringAsync(id, run.Cutoff, ct);
        if (!expiring.IsPositive) return Money.Zero;
        if (!membership.ExpireCredits(expiring, run.Cutoff, new PostingContext(clock.GetUtcNow(), run.Caller)).TryGetValue(out var posting, out var rejected)) return rejected;

        memberships.Append(posting);
        return await memberships.TrySaveChangesAsync(ct) ? expiring : MembershipErrors.ConcurrentUpdate;
    }

    private async Task<Money> ExpiringAsync(MembershipId id, BillingPeriod cutoff, CancellationToken ct) =>
        CreditExpiry.Expiring(await memberships.ExpiryTotalsOfAsync(LedgerAccount.Member(id), cutoff.EndsAt, ct));

    private sealed class RunTally
    {
        private readonly List<MemberExpiryFailure> failures = [];
        private int expired;
        private long expiredCents;

        public void Record(MembershipId id, Result<Money> outcome)
        {
            if (!outcome.TryGetValue(out Money amount, out Error? error))
            {
                failures.Add(new MemberExpiryFailure(id, $"{error.Code}: {error.Message}"));
                return;
            }
            if (!amount.IsPositive) return;

            expired++;
            expiredCents += amount.Cents;
        }

        public CreditExpiryReport ReportFor(BillingPeriod cutoff) => new(cutoff, expired, expiredCents, [.. failures]);
    }
}
