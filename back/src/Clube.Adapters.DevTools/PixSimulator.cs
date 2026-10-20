using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clube.Adapters.Http;
using Clube.Adapters.Partners;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Reconciliation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Clube.Adapters.DevTools;

// What came back from delivering a simulated event: Result is the webhook's own answer, present only when it accepted it.
internal sealed record SimulatedPix(string EndToEndId, LoopbackAnswer Answer, string? Result);

// Plays the payment provider for a demo: builds the event, signs it the way the provider does and delivers it through the
// real webhook (see PixWebhookLoopback). It also keeps the fake PSP statement in step, so the day's reconciliation of
// what it simulated comes out clean, as it would for money that really moved.
internal sealed class PixSimulator(PixWebhookLoopback webhook, ConfigurablePspSettlements psp, TimeProvider clock, IConfiguration configuration)
{
    private const string Alphanumerics = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string OwnIspb = "00000000";
    private const int RandomSuffixLength = 11;

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly string[] MoneyReceivedResults = ["credited", "already_credited", "duplicate_payment"];
    private static readonly string[] RefundedResults = ["refunded", "already_refunded"];

    public async Task<SimulatedPix> PayAsync(Guid membershipId, string period, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        string endToEndId = NewEndToEndId('E', now);
        var received = new PixReceived(endToEndId, membershipId, period, Membership.MonthlyAllowance.Cents, now);

        SimulatedPix outcome = await DeliverAsync(received, endToEndId, now, ct);
        // A duplicate payment is real money at the PSP that the ledger did not credit: the statement must show it.
        if (outcome.Result is not null && MoneyReceivedResults.Contains(outcome.Result))
            psp.Add(new PspSettlement(PaymentReference.FromStored(endToEndId), Membership.MonthlyAllowance, now, Returned: false));
        return outcome;
    }

    public async Task<SimulatedPix> RefundAsync(string originalEndToEndId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        string endToEndId = NewEndToEndId('D', now);
        var refunded = new PixRefunded(endToEndId, originalEndToEndId, now);

        SimulatedPix outcome = await DeliverAsync(refunded, endToEndId, now, ct);
        if (outcome.Result is not null && RefundedResults.Contains(outcome.Result)) psp.MarkReturned(PaymentReference.FromStored(originalEndToEndId));
        return outcome;
    }

    private async Task<SimulatedPix> DeliverAsync(PixWebhookEvent pixEvent, string endToEndId, DateTimeOffset now, CancellationToken ct)
    {
        LoopbackAnswer answer = await webhook.DeliverAsync(Sign(pixEvent, now), ct);
        string? result = answer.StatusCode == StatusCodes.Status200OK
            ? JsonSerializer.Deserialize<PixWebhookResponse>(answer.Body, Wire)?.Result
            : null;
        return new SimulatedPix(endToEndId, answer, result);
    }

    // The secret is read at signing time, as the provider holds its own copy: a secret that differs from the API's is
    // refused by the real signature check, which is how the tests prove the simulated path is not a shortcut.
    private SignedDelivery Sign(PixWebhookEvent pixEvent, DateTimeOffset now)
    {
        string secret = configuration.GetSection(PspOptions.Section).Get<PspOptions>()?.WebhookSecret
            ?? throw new InvalidOperationException($"{PspOptions.Section}:WebhookSecret is not configured.");
        string body = JsonSerializer.Serialize(pixEvent, Wire);
        string timestamp = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        string signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
        return new SignedDelivery(body, timestamp, signature);
    }

    // The Pix id format: a letter (E payment, D refund), the 8-digit ISPB, the UTC minute and 11 random characters (32 in all).
    private static string NewEndToEndId(char kind, DateTimeOffset now) =>
        $"{kind}{OwnIspb}{now:yyyyMMddHHmm}{RandomNumberGenerator.GetString(Alphanumerics, RandomSuffixLength)}";
}
