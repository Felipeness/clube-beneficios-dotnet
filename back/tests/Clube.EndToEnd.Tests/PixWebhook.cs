using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clube.Adapters.Http;

namespace Clube.EndToEnd.Tests;

// Plays the payment provider: builds an event, signs it the way the provider does and delivers it.
// The signature is computed here independently of the adapter, so a change to the scheme breaks these tests.
internal static class PixWebhook
{
    public const string Path = "/webhooks/pix";

    public static object Received(Guid membershipId, string period, string? endToEndId = null, long amountCents = 2000) => new
    {
        type = "pix.received",
        endToEndId = endToEndId ?? NewEndToEndId(),
        membershipId,
        period,
        amountCents,
        paidAt = "2026-10-20T14:59:00Z",
    };

    public static object Refunded(string originalEndToEndId) => new
    {
        type = "pix.refunded",
        endToEndId = NewEndToEndId(),
        originalEndToEndId,
        refundedAt = "2026-10-20T16:00:00Z",
    };

    public static string NewEndToEndId() => $"E{Guid.NewGuid():N}";

    public static string Sign(string secret, string timestamp, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));

    public static string TimestampOf(DateTimeOffset moment) => moment.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    public static Task<HttpResponseMessage> DeliverAsync(ClubeApiFactory factory, HttpClient client, object pixEvent)
    {
        string body = JsonSerializer.Serialize(pixEvent);
        string timestamp = TimestampOf(factory.Clock.GetUtcNow());
        return SendAsync(client, body, timestamp, Sign(ClubeApiFactory.WebhookSecret, timestamp, body));
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, string body, string? timestamp, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (timestamp is not null) request.Headers.Add("X-Pix-Timestamp", timestamp);
        if (signature is not null) request.Headers.Add("X-Pix-Signature", signature);
        return client.SendAsync(request);
    }

    public static async Task<string> ResultOfAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = await response.Content.ReadFromJsonAsync<PixWebhookResponse>();
        Assert.NotNull(answer);
        return answer.Result;
    }

    // What every test that only needs money on the account uses instead of the removed public credit route.
    public static async Task CreditAsync(ClubeApiFactory factory, HttpClient client, Guid membershipId, string period) =>
        Assert.Equal("credited", await ResultOfAsync(await DeliverAsync(factory, client, Received(membershipId, period))));
}
