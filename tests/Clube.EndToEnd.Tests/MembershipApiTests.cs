using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clube.Adapters.Http;
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
        using var member = factory.CreateClientAsMember(created.Id);
        string membership = $"/memberships/{created.Id}";

        await PixWebhook.CreditAsync(factory, client, created.Id, "2026-10");
        var credited = await member.GetFromJsonAsync<MembershipView>(membership, Json);
        await PostAsync<MembershipView>(member, $"{membership}/redemptions", new { rewardId = "cinema-ticket" }, idempotencyKey: "order-1");
        var retriedRedemption = await PostAsync<MembershipView>(member, $"{membership}/redemptions", new { rewardId = "cinema-ticket" }, idempotencyKey: "order-1");
        var statement = await member.GetFromJsonAsync<StatementResponse>($"{membership}/statement", Json);

        Assert.Equal("***.982.247-**", created.Holder);
        Assert.NotNull(credited);
        Assert.Equal(2000, credited.BalanceCents);
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

        client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");
        await PixWebhook.CreditAsync(factory, client, created.Id, "2026-10");
        using var member = factory.CreateClientAsMember(created.Id);
        var statement = await member.GetFromJsonAsync<StatementResponse>($"/memberships/{created.Id}/statement", Json);

        Assert.Equal(traceId, factory.QueryScalar("SELECT CorrelationId FROM ledger_transactions"));
        Assert.Equal("PaymentProvider", factory.QueryScalar("SELECT ActorKind FROM ledger_transactions"));
        Assert.NotNull(statement);
        var line = Assert.Single(statement.Lines);
        Assert.Equal(ActorKind.PaymentProvider, line.ActorKind);
        Assert.Equal("pix", line.ActorId);
    }

    [Fact]
    public async Task RejectsReusedIdempotencyKeyForADifferentReward()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var created = await SubscribeAsync(client);
        using var member = factory.CreateClientAsMember(created.Id);
        string membership = $"/memberships/{created.Id}";
        await PixWebhook.CreditAsync(factory, client, created.Id, "2026-10");
        await PostAsync<MembershipView>(member, $"{membership}/redemptions", new { rewardId = "cinema-ticket" }, idempotencyKey: "order-1");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{membership}/redemptions") { Content = JsonContent.Create(new { rewardId = "thermal-bottle" }) };
        request.Headers.Add("Idempotency-Key", "order-1");
        var response = await member.SendAsync(request);

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
        using var member = factory.CreateClientAsMember(membership.Id);

        var response = await member.PostAsJsonAsync($"/memberships/{membership.Id}/redemptions", new { rewardId = "cinema-ticket" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string, object, HttpStatusCode> InvalidRequests => new()
    {
        { "/memberships", new { cpf = "111.111.111-11" }, HttpStatusCode.BadRequest },
        { "/memberships", new { cpf = (string?)null }, HttpStatusCode.BadRequest },
        { "/memberships", new { }, HttpStatusCode.BadRequest },
        { "{membership}/redemptions", new { rewardId = "yacht" }, HttpStatusCode.NotFound },
        { "{membership}/redemptions", new { rewardId = "cinema-ticket" }, HttpStatusCode.UnprocessableEntity },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task MapsRejectionsToProblemStatus(string path, object body, HttpStatusCode expected)
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var membership = await SubscribeAsync(client);
        using var member = factory.CreateClientAsMember(membership.Id);

        var response = await SendAsync(path.StartsWith("{membership}", StringComparison.Ordinal) ? member : client, path.Replace("{membership}", $"/memberships/{membership.Id}"), body, "any-key");

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
        await PixWebhook.CreditAsync(factory, client, membership.Id, "2026-10");

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
    }

    [Fact]
    public async Task PagesTheStatementWithOpaqueCursorsUntilTheEnd()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var created = await SubscribeAsync(client);
        await CreditMonthsAsync(factory, client, created.Id, 3);
        using var member = factory.CreateClientAsMember(created.Id);
        string statementPath = $"/memberships/{created.Id}/statement";

        var walked = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await member.GetFromJsonAsync<StatementResponse>($"{statementPath}?limit=1" + (cursor is null ? "" : $"&cursor={cursor}"), Json);
            Assert.NotNull(page);
            walked.Add(Assert.Single(page.Lines).TransactionId);
            Assert.Equal(6000, page.BalanceCents);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(3, walked.Distinct().Count());
    }

    [Theory]
    [InlineData("?limit=101")]
    [InlineData("?limit=0")]
    [InlineData("?cursor=not-a-cursor!")]
    [InlineData("?cursor=YWJj")]
    public async Task RejectsInvalidStatementPaging(string query)
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var created = await SubscribeAsync(client);
        using var member = factory.CreateClientAsMember(created.Id);

        var response = await member.GetAsync($"/memberships/{created.Id}/statement{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DocumentsStatementPagingInOpenApi()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        JsonElement statement = document.RootElement
            .GetProperty("paths").GetProperty("/memberships/{id}/statement").GetProperty("get");

        var parameters = statement.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString());
        Assert.Contains("limit", parameters);
        Assert.Contains("cursor", parameters);
        Assert.True(statement.GetProperty("responses").TryGetProperty("400", out _), "statement must document invalid paging");
    }

    [Fact]
    public async Task ReversesARedemptionAppendingToTheStatementAndKeepingTheLedgerImmutable()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var created = await SubscribeAsync(client);
        using var member = factory.CreateClientAsMember(created.Id);
        using var staff = factory.CreateClientAsOperator();
        string membership = $"/memberships/{created.Id}";
        await PixWebhook.CreditAsync(factory, client, created.Id, "2026-10");
        await PostAsync<MembershipView>(member, $"{membership}/redemptions", new { rewardId = "cinema-ticket" }, idempotencyKey: "order-1");
        var before = await member.GetFromJsonAsync<StatementResponse>($"{membership}/statement", Json);
        Assert.NotNull(before);
        Guid redemption = before.Lines.Single(line => line.AmountCents == -1000).TransactionId;

        var reversed = await PostAsync<MembershipView>(staff, $"{membership}/transactions/{redemption}/reversal", new { });
        var again = await PostAsync<MembershipView>(staff, $"{membership}/transactions/{redemption}/reversal", new { });
        var after = await member.GetFromJsonAsync<StatementResponse>($"{membership}/statement", Json);

        Assert.Equal(2000, reversed.BalanceCents);
        Assert.Equal(2000, again.BalanceCents);
        Assert.NotNull(after);
        Assert.Equal([1000L, -1000L, 2000L], after.Lines.Select(line => line.AmountCents));
        Assert.Throws<SqliteException>(() => factory.ExecuteSql("DELETE FROM ledger_transactions"));
    }

    [Fact]
    public async Task ReportsReversalOfUnknownTransactionAndDocumentsItInOpenApi()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var created = await SubscribeAsync(client);
        string path = $"/memberships/{created.Id}/transactions/{Guid.NewGuid()}/reversal";

        using var staff = factory.CreateClientAsOperator();
        var response = await SendAsync(staff, path, new { }, idempotencyKey: null);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        JsonElement responses = document.RootElement
            .GetProperty("paths").GetProperty("/memberships/{id}/transactions/{transactionId}/reversal").GetProperty("post").GetProperty("responses");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(responses.TryGetProperty("404", out _), "reversal must document unknown transactions");
        Assert.True(responses.TryGetProperty("422", out _), "reversal must document reversing a reversal");
    }

    // The credit window only accepts periods up to the current month, so the clock walks forward with them.
    private static async Task CreditMonthsAsync(ClubeApiFactory factory, HttpClient client, Guid membershipId, int months)
    {
        DateTimeOffset start = factory.Clock.GetUtcNow();
        for (int month = 0; month < months; month++)
        {
            var moment = start.AddMonths(month);
            factory.Clock.SetUtcNow(moment);
            await PixWebhook.CreditAsync(factory, client, membershipId, $"{moment:yyyy-MM}");
        }
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
