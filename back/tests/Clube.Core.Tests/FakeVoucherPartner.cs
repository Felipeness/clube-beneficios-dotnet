using Clube.Core.Ports.Driven;

namespace Clube.Core.Tests;

// Plays the reward partner: fails the first calls it is told to, hangs when asked to, and records what got through.
internal sealed class FakeVoucherPartner(int failuresBeforeSuccess = 0, bool hangs = false) : IForIssuingVouchers
{
    private int remainingFailures = failuresBeforeSuccess;

    public List<VoucherRequest> Issued { get; } = [];

    public List<Guid> Cancelled { get; } = [];

    public int Calls { get; private set; }

    public async Task IssueAsync(VoucherRequest request, CancellationToken ct)
    {
        await AnswerAsync(ct);
        Issued.Add(request);
    }

    public async Task CancelAsync(Guid voucherKey, CancellationToken ct)
    {
        await AnswerAsync(ct);
        Cancelled.Add(voucherKey);
    }

    private async Task AnswerAsync(CancellationToken ct)
    {
        Calls++;
        if (hangs) await Task.Delay(Timeout.Infinite, ct);
        if (remainingFailures-- > 0) throw new HttpRequestException("partner returned 503");
    }
}
