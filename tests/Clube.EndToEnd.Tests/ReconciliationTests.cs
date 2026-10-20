using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clube.Adapters.Http;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Reconciliation;
using Clube.Core.Ports.Driving;

namespace Clube.EndToEnd.Tests;

public class ReconciliationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private sealed record LedgerHealth(string Status, long? DifferenceCents);

    private static async Task<Guid> SubscribeAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/memberships", new { cpf = "529.982.247-25" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<MembershipView>(Json);
        Assert.NotNull(created);
        return created.Id;
    }

    private static async Task<LedgerHealth> LedgerHealthAsync(ClubeApiFactory factory, HttpStatusCode expected)
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync("/health/ledger");
        Assert.Equal(expected, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<LedgerHealth>(Json);
        Assert.NotNull(health);
        return health;
    }

    private static async Task<ReconciliationResponse> ReconcileAsync(HttpClient staff, string date)
    {
        var report = await staff.GetFromJsonAsync<ReconciliationResponse>($"/operations/reconciliation?date={date}", Json);
        Assert.NotNull(report);
        return report;
    }

    private static PspSettlement Settled(string endToEndId, long cents, string settledAt = "2026-10-20T14:59:00Z", bool returned = false) => new(
        PaymentReference.FromStored(endToEndId), Money.FromCents(cents), DateTimeOffset.Parse(settledAt), returned);

    private static async Task<StatementResponse> StatementAsync(ClubeApiFactory factory, Guid id)
    {
        using var member = factory.CreateClientAsMember(id);
        var statement = await member.GetFromJsonAsync<StatementResponse>($"/memberships/{id}/statement", Json);
        Assert.NotNull(statement);
        return statement;
    }

    [Fact]
    public async Task KeepsTheLedgerBalancedThroughCreditRedemptionReversalAndExpiry()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);
        await PixWebhook.CreditAsync(factory, client, id, "2026-10");
        using var member = factory.CreateClientAsMember(id);
        var redeem = new HttpRequestMessage(HttpMethod.Post, $"/memberships/{id}/redemptions") { Content = JsonContent.Create(new { rewardId = "cinema-ticket" }) };
        redeem.Headers.Add("Idempotency-Key", "order-1");
        Assert.Equal(HttpStatusCode.OK, (await member.SendAsync(redeem)).StatusCode);
        Guid redemption = (await StatementAsync(factory, id)).Lines[0].TransactionId;
        using var staff = factory.CreateClientAsOperator();
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/memberships/{id}/transactions/{redemption}/reversal", new { })).StatusCode);
        factory.Clock.SetUtcNow(DateTimeOffset.Parse("2027-11-20T15:00:00Z"));
        await factory.RunCreditExpiryAsync();

        var health = await LedgerHealthAsync(factory, HttpStatusCode.OK);

        Assert.Equal(new LedgerHealth("Healthy", 0), health);
    }

    [Fact]
    public async Task ReportsUnhealthyWithTheDifferenceWhenAnEntryIsWrittenAroundTheDomain()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        await PixWebhook.CreditAsync(factory, client, await SubscribeAsync(client), "2026-10");
        // INSERT is not blocked by the append-only triggers, which is exactly what makes this invariant worth probing.
        factory.ExecuteSql("INSERT INTO ledger_entries (TransactionId, Account, SignedCents) VALUES ((SELECT Id FROM ledger_transactions LIMIT 1), 'clube:rogue', 750)");

        var health = await LedgerHealthAsync(factory, HttpStatusCode.ServiceUnavailable);

        Assert.Equal(new LedgerHealth("Unhealthy", 750), health);
    }

    [Fact]
    public async Task AnswersLivenessAnonymously()
    {
        await using var factory = new ClubeApiFactory();
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReconcilesTheDayWithEveryKindOfDivergence()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);
        await PixWebhook.CreditAsync(factory, client, id, "2026-10");
        string credited = factory.QueryScalar("SELECT ExternalReference FROM ledger_transactions WHERE ExternalReference IS NOT NULL") ?? "";
        factory.Psp.Add(Settled("EPAID", 2000));
        factory.Psp.Add(Settled("EDUPLICATE", 2000, returned: true));
        using var staff = factory.CreateClientAsOperator();

        var report = await ReconcileAsync(staff, "2026-10-20");

        Assert.Equal(
            [(credited, DivergenceKind.MissingAtPsp), ("EPAID", DivergenceKind.MissingInLedger)],
            report.Divergences.OrderBy(divergence => divergence.EndToEndId, StringComparer.Ordinal).Select(divergence => (divergence.EndToEndId, divergence.Kind)));
    }

    [Fact]
    public async Task ReportsAMatchingDayAsClean()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);
        Assert.Equal("credited", await PixWebhook.ResultOfAsync(await PixWebhook.DeliverAsync(factory, client, PixWebhook.Received(id, "2026-10", "EMATCHED"))));
        factory.Psp.Add(Settled("EMATCHED", 2000));
        using var staff = factory.CreateClientAsOperator();

        var report = await ReconcileAsync(staff, "2026-10-20");

        Assert.Equal((1, 1, 0), (report.SettledAtPsp, report.CreditedInLedger, report.Divergences.Count));
    }

    [Fact]
    public async Task AssignsACreditPostedAfterUtcMidnightToTheSaoPauloDay()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);
        factory.Clock.SetUtcNow(DateTimeOffset.Parse("2026-10-21T02:30:00Z"));
        Assert.Equal("credited", await PixWebhook.ResultOfAsync(await PixWebhook.DeliverAsync(factory, client, PixWebhook.Received(id, "2026-10", "ELATE"))));
        factory.Psp.Add(Settled("ELATE", 2000, settledAt: "2026-10-21T02:29:00Z"));
        using var staff = factory.CreateClientAsOperator();

        var thatDay = await ReconcileAsync(staff, "2026-10-20");
        var nextDay = await ReconcileAsync(staff, "2026-10-21");

        Assert.Equal((1, 1, 0), (thatDay.SettledAtPsp, thatDay.CreditedInLedger, thatDay.Divergences.Count));
        Assert.Equal((0, 0), (nextDay.SettledAtPsp, nextDay.CreditedInLedger));
    }

    [Fact]
    public async Task RefusesTheReportWithoutAnOperatorToken()
    {
        await using var factory = new ClubeApiFactory();
        using var anonymous = factory.CreateClient();
        using var member = factory.CreateClientAsMember(Guid.NewGuid());
        using var staff = factory.CreateClientAsOperator();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/operations/reconciliation?date=2026-10-20")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/operations/reconciliation?date=2026-10-20")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/operations/reconciliation?date=2026-10-20")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?date=20-10-2026")]
    [InlineData("?date=2026-13-40")]
    public async Task RejectsAMissingOrMalformedDate(string query)
    {
        await using var factory = new ClubeApiFactory();
        using var staff = factory.CreateClientAsOperator();

        var response = await staff.GetAsync($"/operations/reconciliation{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
