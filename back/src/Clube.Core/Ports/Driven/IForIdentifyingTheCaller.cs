using Clube.Core.Domain.Ledger;

namespace Clube.Core.Ports.Driven;

/// <summary>
/// Driven port: who is calling right now and which request this is, as known by whatever drives the hexagon
/// (an HTTP request today, a job run or webhook delivery later). Keeps identity out of every use case signature.
/// </summary>
public interface IForIdentifyingTheCaller
{
    CallerContext Current { get; }
}
