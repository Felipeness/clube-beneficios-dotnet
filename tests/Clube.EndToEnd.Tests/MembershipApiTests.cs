using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clube.Core.Domain.Ledger;
using Clube.Core.Ports.Driving;
using Microsoft.Data.Sqlite;

namespace Clube.EndToEnd.Tests;

public class MembershipApiTests
{
    private const string ValidCpf = "529.982.247-25";

    // Mirrors the API contract: enums travel by name.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task CreditsAndRedeemsThroughTheLedgerIdempotently()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var created = await SubscribeAsync(client);
        string membership = $"/memberships/{created.Id}";

        await PostAsync<MembershipView>(client, $"{membership}/allowance-credits", new { period = "2026-10" });
        var retriedCredit = await PostAsync<MembershipView>(client, $"{membership}/allowance-credits", new { period = "2026-10" });
        await PostAsync<MembershipView>(client, $"{membership}/redemptions", new { rewardId = "cinema-ticket" }, idempotencyKey: "order-1");
        var retriedRedemption = await PostAsync<MembershipView>(client, $"{membership}/redemptions", new { rewardId = "cinema-ticket" }, idempotencyKey: "order-1");
        var statement = await client.GetFromJsonAsync<StatementView>($"{membership}/statement", Json);

        Assert.Equal("***.982.247-**", created.Holder);
        Assert.Equal(2000, retriedCredit.BalanceCents);
        Assert.Equal(1000, retriedRedemption.BalanceCents);
        Assert.NotNull(statement);
        Assert.Equal([-1000L, 2000L], statement.Lines.Select(line => line.AmountCents));
    }

    [Fact]
    public async Task RecordsTheRequestTraceAsCorrelationAndTheActorOnTheStatement()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var created = await SubscribeAsync(client);
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/memberships/{created.Id}/allowance-credits") { Content = JsonContent.Create(new { period = "2026-10" }) };
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        var statement = await client.GetFromJsonAsync<StatementView>($"/memberships/{created.Id}/statement", Json);

        Assert.Equal(traceId, factory.QueryScalar("SELECT CorrelationId FROM ledger_transactions"));
        Assert.Equal("Anonymous", factory.QueryScalar("SELECT ActorKind FROM ledger_transactions"));
        Assert.NotNull(statement);
        Assert.Equal(ActorKind.Anonymous, Assert.Single(statement.Lines).ActorKind);
    }

    [Fact]
    public async Task RejectsReusedIdempotencyKeyForADifferentReward()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var created = await SubscribeAsync(client);
        string membership = $"/memberships/{created.Id}";
        await PostAsync<MembershipView>(client, $"{membership}/allowance-credits", new { period = "2026-10" });
        await PostAsync<MembershipView>(client, $"{membership}/redemptions", new { rewardId = "cinema-ticket" }, idempotencyKey: "order-1");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{membership}/redemptions") { Content = JsonContent.Create(new { rewardId = "thermal-bottle" }) };
        request.Headers.Add("Idempotency-Key", "order-1");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RejectsSecondMembershipForSameHolder()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        await SubscribeAsync(client);

        var response = await client.PostAsJsonAsync("/memberships", new { cpf = ValidCpf });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RequiresIdempotencyKeyToRedeem()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var membership = await SubscribeAsync(client);

        var response = await client.PostAsJsonAsync($"/memberships/{membership.Id}/redemptions", new { rewardId = "cinema-ticket" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string, object, HttpStatusCode> InvalidRequests => new()
    {
        { "/memberships", new { cpf = "111.111.111-11" }, HttpStatusCode.BadRequest },
        { "/memberships", new { cpf = (string?)null }, HttpStatusCode.BadRequest },
        { "/memberships", new { }, HttpStatusCode.BadRequest },
        { $"/memberships/{Guid.Empty}/redemptions", new { rewardId = "cinema-ticket" }, HttpStatusCode.NotFound },
        { "{membership}/redemptions", new { rewardId = "yacht" }, HttpStatusCode.NotFound },
        { "{membership}/redemptions", new { rewardId = "cinema-ticket" }, HttpStatusCode.UnprocessableEntity },
        { "{membership}/allowance-credits", new { period = "2026-13" }, HttpStatusCode.BadRequest },
        { "{membership}/allowance-credits", new { period = "2026-11" }, HttpStatusCode.UnprocessableEntity },
        { "{membership}/allowance-credits", new { period = "2026-09" }, HttpStatusCode.UnprocessableEntity },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task MapsRejectionsToProblemStatus(string path, object body, HttpStatusCode expected)
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var membership = await SubscribeAsync(client);

        var response = await SendAsync(client, path.Replace("{membership}", $"/memberships/{membership.Id}"), body, "any-key");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("UPDATE ledger_entries SET SignedCents = 999999")]
    [InlineData("DELETE FROM ledger_entries")]
    [InlineData("DELETE FROM ledger_transactions")]
    public async Task DatabaseRefusesToRewriteTheLedger(string sql)
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var membership = await SubscribeAsync(client);
        await PostAsync<MembershipView>(client, $"/memberships/{membership.Id}/allowance-credits", new { period = "2026-10" });

        var refused = Assert.Throws<SqliteException>(() => factory.ExecuteSql(sql));

        Assert.Contains("append-only", refused.Message);
    }

    [Fact]
    public async Task DocumentsRejectionsInOpenApi()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        JsonElement responses = document.RootElement
            .GetProperty("paths").GetProperty("/memberships").GetProperty("post").GetProperty("responses");

        Assert.True(responses.TryGetProperty("400", out _), "POST /memberships must document invalid CPF");
        Assert.True(responses.TryGetProperty("409", out _), "POST /memberships must document duplicate holder");

        JsonElement credit = document.RootElement
            .GetProperty("paths").GetProperty("/memberships/{id}/allowance-credits").GetProperty("post").GetProperty("responses");
        Assert.True(credit.TryGetProperty("422", out _), "allowance credit must document periods outside the membership window");
    }

    private static Task<MembershipView> SubscribeAsync(HttpClient client) =>
        PostAsync<MembershipView>(client, "/memberships", new { cpf = ValidCpf }, expected: HttpStatusCode.Created);

    private static async Task<T> PostAsync<T>(
        HttpClient client, string path, object body, string? idempotencyKey = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await SendAsync(client, path, body, idempotencyKey);
        Assert.Equal(expected, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<T>(Json);
        Assert.NotNull(payload);
        return payload;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }
}
