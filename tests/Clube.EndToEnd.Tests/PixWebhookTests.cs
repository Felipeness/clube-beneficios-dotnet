using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Hosting;

namespace Clube.EndToEnd.Tests;

public class PixWebhookTests
{
    private static async Task<Guid> SubscribeAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/memberships", new { cpf = "529.982.247-25" });
        var created = await response.Content.ReadFromJsonAsync<MembershipView>();
        Assert.NotNull(created);
        return created.Id;
    }

    private static async Task<long> BalanceAsync(HttpClient client, Guid membershipId)
    {
        var view = await client.GetFromJsonAsync<MembershipView>($"/memberships/{membershipId}");
        Assert.NotNull(view);
        return view.BalanceCents;
    }

    [Fact]
    public async Task CreditsTheAllowanceAndRecordsTheEndToEndId()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid membershipId = await SubscribeAsync(client);
        string endToEndId = PixWebhook.NewEndToEndId();

        string result = await PixWebhook.ResultOfAsync(await PixWebhook.DeliverAsync(factory, client, PixWebhook.Received(membershipId, "2026-10", endToEndId)));

        Assert.Equal("credited", result);
        Assert.Equal(2000, await BalanceAsync(client, membershipId));
        Assert.Equal(endToEndId, factory.QueryScalar("SELECT ExternalReference FROM ledger_transactions"));
    }

    // Each case breaks exactly one thing a genuine delivery has; all must look the same from outside.
    public static TheoryData<string, int, string?, bool> UnauthenticDeliveries => new()
    {
        { "wrong secret", 0, "another-secret-another-secret-another-secret", true },
        { "missing signature", 0, null, true },
        { "missing timestamp", 0, ClubeApiFactory.WebhookSecret, false },
        { "timestamp too old", -301, ClubeApiFactory.WebhookSecret, true },
        { "timestamp in the future", 301, ClubeApiFactory.WebhookSecret, true },
    };

    [Theory]
    [MemberData(nameof(UnauthenticDeliveries))]
    public async Task RefusesUnauthenticDeliveriesWithoutCrediting(string scenario, int skewSeconds, string? signingSecret, bool sendsTimestamp)
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid membershipId = await SubscribeAsync(client);
        string body = JsonSerializer.Serialize(PixWebhook.Received(membershipId, "2026-10"));
        string timestamp = PixWebhook.TimestampOf(factory.Clock.GetUtcNow().AddSeconds(skewSeconds));
        string? signature = signingSecret is null ? null : PixWebhook.Sign(signingSecret, timestamp, body);

        var response = await PixWebhook.SendAsync(client, body, sendsTimestamp ? timestamp : null, signature);

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, scenario);
        Assert.Equal(0, await BalanceAsync(client, membershipId));
    }

    [Fact]
    public async Task RefusesABodyChangedAfterSigning()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid membershipId = await SubscribeAsync(client);
        string timestamp = PixWebhook.TimestampOf(factory.Clock.GetUtcNow());
        string signed = JsonSerializer.Serialize(PixWebhook.Received(membershipId, "2026-10"));
        string tampered = signed.Replace("2026-10", "2026-09", StringComparison.Ordinal);

        var response = await PixWebhook.SendAsync(client, tampered, timestamp, PixWebhook.Sign(ClubeApiFactory.WebhookSecret, timestamp, signed));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AcceptsADeliveryAtTheEdgeOfTheTolerance()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid membershipId = await SubscribeAsync(client);
        string body = JsonSerializer.Serialize(PixWebhook.Received(membershipId, "2026-10"));
        string timestamp = PixWebhook.TimestampOf(factory.Clock.GetUtcNow().AddMinutes(-5));

        var response = await PixWebhook.SendAsync(client, body, timestamp, PixWebhook.Sign(ClubeApiFactory.WebhookSecret, timestamp, body));

        Assert.Equal("credited", await PixWebhook.ResultOfAsync(response));
    }

    [Fact]
    public async Task CreditsARedeliveredPaymentOnce()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid membershipId = await SubscribeAsync(client);
        object payment = PixWebhook.Received(membershipId, "2026-10");

        string first = await PixWebhook.ResultOfAsync(await PixWebhook.DeliverAsync(factory, client, payment));
        string again = await PixWebhook.ResultOfAsync(await PixWebhook.DeliverAsync(factory, client, payment));

        Assert.Equal("credited", first);
        Assert.Equal("already_credited", again);
        Assert.Equal(2000, await BalanceAsync(client, membershipId));
    }

    [Fact]
    public async Task AnswersASecondPaymentForAPaidPeriodAsDuplicateWithoutCrediting()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid membershipId = await SubscribeAsync(client);
        await PixWebhook.CreditAsync(factory, client, membershipId, "2026-10");

        string result = await PixWebhook.ResultOfAsync(await PixWebhook.DeliverAsync(factory, client, PixWebhook.Received(membershipId, "2026-10")));

        Assert.Equal("duplicate_payment", result);
        Assert.Equal(2000, await BalanceAsync(client, membershipId));
    }

    [Theory]
    [InlineData(1999, "2026-10", HttpStatusCode.UnprocessableEntity)]
    [InlineData(2000, "2026-11", HttpStatusCode.UnprocessableEntity)]
    [InlineData(2000, "2026-13", HttpStatusCode.BadRequest)]
    public async Task RejectsPaymentsThatCannotPayTheAllowance(long amountCents, string period, HttpStatusCode expected)
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid membershipId = await SubscribeAsync(client);

        var response = await PixWebhook.DeliverAsync(factory, client, PixWebhook.Received(membershipId, period, amountCents: amountCents));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, await BalanceAsync(client, membershipId));
    }

    [Fact]
    public async Task RejectsAPaymentForAnUnknownMembership()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        await SubscribeAsync(client);

        var response = await PixWebhook.DeliverAsync(factory, client, PixWebhook.Received(Guid.NewGuid(), "2026-10"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"endToEndId":"E1"}""")]
    [InlineData("""{"type":"pix.chargeback","endToEndId":"E1"}""")]
    [InlineData("""{"type":"pix.refunded","endToEndId":"E1"}""")]
    public async Task RejectsAnAuthenticPayloadOutsideTheContract(string body)
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        await SubscribeAsync(client);
        string timestamp = PixWebhook.TimestampOf(factory.Clock.GetUtcNow());

        var response = await PixWebhook.SendAsync(client, body, timestamp, PixWebhook.Sign(ClubeApiFactory.WebhookSecret, timestamp, body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RefundReversesTheCreditOnceAndTheBalanceGoesBack()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid membershipId = await SubscribeAsync(client);
        string endToEndId = PixWebhook.NewEndToEndId();
        await PixWebhook.DeliverAsync(factory, client, PixWebhook.Received(membershipId, "2026-10", endToEndId));
        object refund = PixWebhook.Refunded(endToEndId);

        string first = await PixWebhook.ResultOfAsync(await PixWebhook.DeliverAsync(factory, client, refund));
        string again = await PixWebhook.ResultOfAsync(await PixWebhook.DeliverAsync(factory, client, refund));

        Assert.Equal("refunded", first);
        Assert.Equal("already_refunded", again);
        Assert.Equal(0, await BalanceAsync(client, membershipId));
        Assert.Equal("2", factory.QueryScalar("SELECT COUNT(*) FROM ledger_transactions"));
    }

    [Fact]
    public async Task AcknowledgesARefundOfAnUnknownPaymentWithoutTouchingTheLedger()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        await SubscribeAsync(client);

        string result = await PixWebhook.ResultOfAsync(await PixWebhook.DeliverAsync(factory, client, PixWebhook.Refunded(PixWebhook.NewEndToEndId())));

        Assert.Equal("unknown_payment", result);
        Assert.Equal("0", factory.QueryScalar("SELECT COUNT(*) FROM ledger_transactions"));
    }

    [Fact]
    public async Task NoLongerExposesThePublicCreditRoute()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid membershipId = await SubscribeAsync(client);

        var response = await client.PostAsJsonAsync($"/memberships/{membershipId}/allowance-credits", new { period = "2026-10" });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(0, await BalanceAsync(client, membershipId));
    }

    [Fact]
    public async Task DocumentsTheWebhookItsPayloadsAndTheUnauthorizedAnswerInOpenApi()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        JsonElement webhook = document.RootElement.GetProperty("paths").GetProperty(PixWebhook.Path).GetProperty("post");
        string schemas = document.RootElement.GetProperty("components").GetProperty("schemas").GetRawText();
        var headers = webhook.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString());

        Assert.True(webhook.GetProperty("responses").TryGetProperty("401", out _), "webhook must document refused signatures");
        Assert.True(webhook.TryGetProperty("requestBody", out _), "webhook must document its payload");
        Assert.Contains("X-Pix-Signature", headers);
        Assert.Contains("X-Pix-Timestamp", headers);
        Assert.Contains("originalEndToEndId", schemas);
        Assert.Contains("amountCents", schemas);
    }

    [Fact]
    public async Task RefusesToBootOutsideDevelopmentWithoutAWebhookSecret()
    {
        await using var factory = new ClubeApiFactory();
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production").UseSetting("Psp:WebhookSecret", ""));

        var refused = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains("Psp:WebhookSecret", refused.ToString());
    }
}
