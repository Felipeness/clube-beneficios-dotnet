using Clube.Core.Domain.Memberships;

namespace Clube.Core.Ports.Driving;

/// <summary>Driving port for whatever triggers credit expiry (a daily hosted loop today, a test or a manual rerun otherwise).</summary>
public interface IForExpiringCredits
{
    /// <summary>Expires, for every member, what is still unspent of the credits posted up to <paramref name="cutoff"/>. Safe to rerun.</summary>
    Task<CreditExpiryReport> RunAsync(BillingPeriod cutoff, CancellationToken ct);
}

public sealed record MemberExpiryFailure(MembershipId Member, string Error);

/// <summary>What one run did. A failed member is listed and retried by the next run; it never stops the others.</summary>
public sealed record CreditExpiryReport(BillingPeriod Cutoff, int Expired, long ExpiredCents, IReadOnlyList<MemberExpiryFailure> Failures);
