using Clube.Core.Domain.Ledger;
using Clube.Core.Ports.Driven;

namespace Clube.Core.Tests;

internal sealed class FakeCaller(Actor actor, string correlationId) : IForIdentifyingTheCaller
{
    public CallerContext Current { get; } = new(actor, correlationId);
}
