namespace Clube.Core.Domain.Ledger;

// The correlation id ties a posting to the request (or job run) that caused it, for dispute investigation.
public sealed record CallerContext(Actor Actor, string CorrelationId);

// When a posting happens and on whose behalf: the two facts every ledger transaction is stamped with.
public sealed record PostingContext(DateTimeOffset At, CallerContext Caller);
