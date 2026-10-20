using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clube.Adapters.Http;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.DependencyInjection;

namespace Clube.EndToEnd.Tests;

public class CreditExpiryTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<Guid> SubscribeAsync(HttpClient client, string cpf)
    {
        var response = await client.PostAsJsonAsync("/memberships", new { cpf });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<MembershipView>(Json);
        Assert.NotNull(created);
        return created.Id;
    }

    private static async Task<StatementResponse> StatementAsync(ClubeApiFactory factory, Guid membershipId)
    {
        using var member = factory.CreateClientAsMember(membershipId);
        var statement = await member.GetFromJsonAsync<StatementResponse>($"/memberships/{membershipId}/statement", Json);
        Assert.NotNull(statement);
        return statement;
    }

    [Fact]
    public async Task ExpiresOldCreditThroughTheRunnerAndShowsItOnTheStatement()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client, "529.982.247-25");
        await PixWebhook.CreditAsync(factory, client, id, "2026-10");
        factory.Clock.SetUtcNow(DateTimeOffset.Parse("2027-11-20T15:00:00Z"));
        await PixWebhook.CreditAsync(factory, client, id, "2027-11");

        var first = await factory.RunCreditExpiryAsync();
        var second = await factory.RunCreditExpiryAsync();
        var statement = await StatementAsync(factory, id);

        Assert.Equal(2000, first.ExpiredCents);
        Assert.Equal(0, second.Expired);
        Assert.Equal(2000, statement.BalanceCents);
        var expiry = statement.Lines[0];
        Assert.Equal("Credit expired (credits up to 2026-10)", expiry.Description);
        Assert.Equal(-2000, expiry.AmountCents);
        Assert.Equal(ActorKind.System, expiry.ActorKind);
        Assert.Equal("credit-expiry", expiry.ActorId);
        Assert.Equal("2000", factory.QueryScalar("SELECT SUM(SignedCents) FROM ledger_entries WHERE Account = 'clube:breakage'"));
    }

    [Fact]
    public async Task LetsAnOperatorReverseAnExpiry()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client, "529.982.247-25");
        await PixWebhook.CreditAsync(factory, client, id, "2026-10");
        factory.Clock.SetUtcNow(DateTimeOffset.Parse("2027-11-20T15:00:00Z"));
        await factory.RunCreditExpiryAsync();
        Guid expiry = (await StatementAsync(factory, id)).Lines[0].TransactionId;

        using var staff = factory.CreateClientAsOperator();
        var response = await staff.PostAsJsonAsync($"/memberships/{id}/transactions/{expiry}/reversal", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2000, (await StatementAsync(factory, id)).BalanceCents);
    }

    // The keyset runs on raw SQL over a TEXT column, so the cursor read back from one page must open the next one.
    [Fact]
    public async Task PagesMembershipIdsInTheStoreAfterTheCursor()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid[] subscribed = [await SubscribeAsync(client, "529.982.247-25"), await SubscribeAsync(client, "111.444.777-35")];

        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IForStoringMemberships>();
        var firstPage = await store.MembershipIdsAfterAsync(null, 1, default);
        var secondPage = await store.MembershipIdsAfterAsync(firstPage[0], 1, default);
        var lastPage = await store.MembershipIdsAfterAsync(secondPage[0], 1, default);

        Assert.Equal(subscribed.Order(), new[] { firstPage[0].Value, secondPage[0].Value }.Order());
        Assert.Empty(lastPage);
    }

    [Fact]
    public async Task SumsOnlyCreditsPostedBeforeTheCutoffButEveryDebit()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client, "529.982.247-25");
        await PixWebhook.CreditAsync(factory, client, id, "2026-10");
        factory.Clock.SetUtcNow(DateTimeOffset.Parse("2026-11-20T15:00:00Z"));
        await PixWebhook.CreditAsync(factory, client, id, "2026-11");
        using var member = factory.CreateClientAsMember(id);
        var redeem = new HttpRequestMessage(HttpMethod.Post, $"/memberships/{id}/redemptions") { Content = JsonContent.Create(new { rewardId = "cinema-ticket" }) };
        redeem.Headers.Add("Idempotency-Key", "order-1");
        Assert.Equal(HttpStatusCode.OK, (await member.SendAsync(redeem)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IForStoringMemberships>();
        var totals = await store.ExpiryTotalsOfAsync(LedgerAccount.Member(new MembershipId(id)), BillingPeriod.At(DateTimeOffset.Parse("2026-10-20T15:00:00Z")).EndsAt, default);

        Assert.Equal(new ExpiryTotals(Money.FromCents(2000), Money.FromCents(1000)), totals);
    }
}
