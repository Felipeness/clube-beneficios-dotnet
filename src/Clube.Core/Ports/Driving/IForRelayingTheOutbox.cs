using Clube.Core.Domain.Vouchers;

namespace Clube.Core.Ports.Driving;

/// <summary>Driving port for whatever keeps the outbox moving (a hosted loop today, a test or a manual replay otherwise).</summary>
public interface IForRelayingTheOutbox
{
    /// <summary>Attempts every due message once and returns each one as recorded afterwards.</summary>
    Task<IReadOnlyList<OutboxMessage>> DispatchBatchAsync(CancellationToken ct);
}
