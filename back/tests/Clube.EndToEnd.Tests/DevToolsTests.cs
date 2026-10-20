using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clube.Adapters.DevTools;
using Clube.Adapters.Http;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Reconciliation;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clube.EndToEnd.Tests;

public class DevToolsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string ReconciledDay = "2026-10-20";

    private static async Task<Guid> SubscribeAsync(HttpClient client)
    {
        var created = await ReadAsync<MembershipView>(await client.PostAsJsonAsync("/memberships", new { cpf = "529.982.247-25" }, Json), HttpStatusCode.Created);
        return created.Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<T>(Json);
        Assert.NotNull(body);
        return body;
    }

    private static async Task<SimulatedPixResponse> PayAsync(HttpClient client, Guid membershipId) =>
        await ReadAsync<SimulatedPixResponse>(await client.PostAsJsonAsync("/dev/pix-payments", new { membershipId, period = "2026-10" }));

    private static async Task<ReconciliationResponse> ReconcileAsync(HttpClient client) =>
        await ReadAsync<ReconciliationResponse>(await client.PostAsJsonAsync("/dev/jobs/reconciliation", new { date = ReconciledDay }));

    private static async Task<StatementResponse> StatementAsync(ClubeApiFactory factory, Guid membershipId)
    {
        using var member = factory.CreateClientAsMember(membershipId);
        return await ReadAsync<StatementResponse>(await member.GetAsync($"/memberships/{membershipId}/statement"));
    }

    private static async Task<HttpClient> ClientWithDevTokenAsync(ClubeApiFactory factory, object tokenRequest)
    {
        var token = await ReadAsync<IssuedToken>(await factory.CreateClient().PostAsJsonAsync("/dev/tokens", tokenRequest));
        return factory.CreateClientWith(token.AccessToken);
    }

    [Fact]
    public async Task IssuesAMemberTokenTheApiAccepts()
    {
        await using var factory = new ClubeApiFactory();
        Guid id = await SubscribeAsync(factory.CreateClient());
        using var member = await ClientWithDevTokenAsync(factory, new { role = "member", membershipId = id });

        var own = await member.GetAsync($"/memberships/{id}");
        var anotherMember = await member.GetAsync($"/memberships/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, anotherMember.StatusCode);
    }

    [Fact]
    public async Task IssuesAnOperatorTokenTheApiAccepts()
    {
        await using var factory = new ClubeApiFactory();
        using var staff = await ClientWithDevTokenAsync(factory, new { role = "operator", name = "ana" });

        var report = await staff.GetAsync($"/operations/reconciliation?date={ReconciledDay}");

        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
    }

    [Fact]
    public async Task ExpiresTheIssuedTokenInEightHoursOfRealTime()
    {
        await using var factory = new ClubeApiFactory();

        var token = await ReadAsync<IssuedToken>(await factory.CreateClient().PostAsJsonAsync("/dev/tokens", new { role = "operator", name = "ana" }));

        Assert.InRange(token.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromHours(7.9), TimeSpan.FromHours(8));
    }

    [Theory]
    [InlineData("""{"role":"member"}""")]
    [InlineData("""{"role":"operator"}""")]
    [InlineData("""{"role":"operator","name":""}""")]
    [InlineData("""{"role":"admin","name":"ana"}""")]
    public async Task RejectsATokenRequestWithoutTheIdentityItNeeds(string body)
    {
        await using var factory = new ClubeApiFactory();

        var response = await factory.CreateClient().PostAsync("/dev/tokens", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreditsASimulatedPixAsTheProviderAndReconcilesTheDayClean()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);

        var payment = await PayAsync(client, id);
        var statement = await StatementAsync(factory, id);
        var report = await ReconcileAsync(client);

        Assert.Equal("credited", payment.Result);
        Assert.Equal(32, payment.EndToEndId.Length);
        Assert.StartsWith("E", payment.EndToEndId, StringComparison.Ordinal);
        Assert.Equal(2000, statement.BalanceCents);
        Assert.Equal(ActorKind.PaymentProvider, statement.Lines[0].ActorKind);
        Assert.Equal((1, 1), (report.SettledAtPsp, report.CreditedInLedger));
        Assert.Empty(report.Divergences);
    }

    [Fact]
    public async Task ReportsASecondPaymentForAPaidPeriodAsMoneyTheLedgerDidNotCredit()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);
        await PayAsync(client, id);

        var second = await PayAsync(client, id);
        var report = await ReconcileAsync(client);

        Assert.Equal("duplicate_payment", second.Result);
        Assert.Equal(DivergenceKind.MissingInLedger, Assert.Single(report.Divergences).Kind);
    }

    [Fact]
    public async Task RefundsASimulatedPixAndKeepsTheReconciliationClean()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);
        var payment = await PayAsync(client, id);

        var refund = await ReadAsync<SimulatedPixResponse>(await client.PostAsJsonAsync("/dev/pix-refunds", new { originalEndToEndId = payment.EndToEndId }));
        var statement = await StatementAsync(factory, id);
        var report = await ReconcileAsync(client);

        Assert.Equal("refunded", refund.Result);
        Assert.StartsWith("D", refund.EndToEndId, StringComparison.Ordinal);
        Assert.Equal(0, statement.BalanceCents);
        Assert.Empty(report.Divergences);
    }

    [Fact]
    public async Task AnswersUnknownPaymentWhenRefundingAnIdTheLedgerNeverSaw()
    {
        await using var factory = new ClubeApiFactory();

        var refund = await ReadAsync<SimulatedPixResponse>(await factory.CreateClient().PostAsJsonAsync("/dev/pix-refunds", new { originalEndToEndId = "ENEVERSEEN" }));

        Assert.Equal("unknown_payment", refund.Result);
    }

    [Fact]
    public async Task RelaysTheWebhooksRefusalOfAnInvalidPeriod()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);

        var response = await client.PostAsJsonAsync("/dev/pix-payments", new { membershipId = id, period = "not-a-period" });
        var report = await ReconcileAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, report.SettledAtPsp);
    }

    [Fact]
    public async Task RefusesASimulatedPixSignedWithAnotherSecretByTheRealSignatureCheck()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);
        // The webhook verifier reads the secret once, on its first delivery; the simulator reads it on every signing.
        await PayAsync(client, id);
        factory.Services.GetRequiredService<IConfiguration>()["Psp:WebhookSecret"] = "another-secret-0123456789abcdef0123456789";

        var response = await client.PostAsJsonAsync("/dev/pix-payments", new { membershipId = id, period = "2026-10" });
        var statement = await StatementAsync(factory, id);
        var report = await ReconcileAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2000, statement.BalanceCents);
        Assert.Equal(1, report.SettledAtPsp);
    }

    [Fact]
    public async Task RunsTheOutboxOnceAndReportsTheBatch()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);
        await PayAsync(client, id);
        using var member = factory.CreateClientAsMember(id);
        member.DefaultRequestHeaders.Add("Idempotency-Key", "redeem-1");
        (await member.PostAsJsonAsync($"/memberships/{id}/redemptions", new { rewardId = "cinema-ticket" })).EnsureSuccessStatusCode();

        var first = await ReadAsync<OutboxRunResponse>(await client.PostAsync("/dev/jobs/outbox", null));
        var second = await ReadAsync<OutboxRunResponse>(await client.PostAsync("/dev/jobs/outbox", null));

        Assert.Equal(new OutboxRunResponse(1, 1, 0, 0), first);
        Assert.Equal(new OutboxRunResponse(0, 0, 0, 0), second);
    }

    [Fact]
    public async Task ExpiresOldCreditOnceTheDevClockIsAdvancedPastItsLifetime()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        Guid id = await SubscribeAsync(client);
        await PayAsync(client, id);
        var early = await ReadAsync<CreditExpiryRunResponse>(await client.PostAsync("/dev/jobs/credit-expiry", null));

        await client.PostAsJsonAsync("/dev/clock", new { advanceDays = 396 });
        var late = await ReadAsync<CreditExpiryRunResponse>(await client.PostAsync("/dev/jobs/credit-expiry", null));
        var statement = await StatementAsync(factory, id);

        Assert.Equal(0, early.Expired);
        Assert.Equal((1, 2000L, "2026-10"), (late.Expired, late.ExpiredCents, late.Cutoff));
        Assert.Empty(late.Failures);
        Assert.Equal(0, statement.BalanceCents);
    }

    [Fact]
    public async Task AdvancesAndResetsTheDevClockOverTheBaseClock()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        DateTimeOffset start = factory.Clock.GetUtcNow();

        var initial = await ReadAsync<ClockResponse>(await client.GetAsync("/dev/clock"));
        var advanced = await ReadAsync<ClockResponse>(await client.PostAsJsonAsync("/dev/clock", new { advanceDays = 30 }));
        var advancedAgain = await ReadAsync<ClockResponse>(await client.PostAsJsonAsync("/dev/clock", new { advanceDays = 5 }));
        var reset = await ReadAsync<ClockResponse>(await client.PostAsJsonAsync("/dev/clock", new { reset = true }));

        Assert.Equal(new ClockResponse(start, 0), initial);
        Assert.Equal(new ClockResponse(start.AddDays(30), 30), advanced);
        Assert.Equal(new ClockResponse(start.AddDays(35), 35), advancedAgain);
        Assert.Equal(new ClockResponse(start, 0), reset);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"advanceDays":0}""")]
    [InlineData("""{"advanceDays":-3}""")]
    [InlineData("""{"advanceDays":3651}""")]
    [InlineData("""{"advanceDays":5,"reset":true}""")]
    [InlineData("""{"reset":false}""")]
    public async Task RejectsAClockRequestOutsideTheSupportedRange(string body)
    {
        await using var factory = new ClubeApiFactory();

        var response = await factory.CreateClient().PostAsync("/dev/clock", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RefusesToLeaveTheClockBeyondTheSupportedOffset()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/dev/clock", new { advanceDays = 3000 });

        var response = await client.PostAsJsonAsync("/dev/clock", new { advanceDays = 651 });
        var clock = await ReadAsync<ClockResponse>(await client.GetAsync("/dev/clock"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(3000, clock.OffsetDays);
    }

    [Fact]
    public async Task AllowsTheExpoOriginOnlyThroughTheDevelopmentCorsPolicy()
    {
        await using var factory = new ClubeApiFactory();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, $"/memberships/{Guid.NewGuid()}/redemptions");
        preflight.Headers.Add("Origin", "http://localhost:8081");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,idempotency-key");

        var allowed = await factory.CreateClient().SendAsync(preflight);

        Assert.True(allowed.IsSuccessStatusCode);
        Assert.Equal("http://localhost:8081", Assert.Single(allowed.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("Idempotency-Key", string.Join(',', allowed.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("POST", "/dev/tokens")]
    [InlineData("POST", "/dev/pix-payments")]
    [InlineData("POST", "/dev/pix-refunds")]
    [InlineData("POST", "/dev/jobs/outbox")]
    [InlineData("POST", "/dev/jobs/credit-expiry")]
    [InlineData("POST", "/dev/jobs/reconciliation")]
    [InlineData("GET", "/dev/clock")]
    [InlineData("POST", "/dev/clock")]
    public async Task ServesNoDevRouteInProduction(string method, string path)
    {
        await using var factory = new ClubeApiFactory();
        factory.MigrateDatabase();
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var response = await production.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AllowsNoCrossOriginInProduction()
    {
        await using var factory = new ClubeApiFactory();
        factory.MigrateDatabase();
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/rewards");
        request.Headers.Add("Origin", "http://localhost:8081");

        var response = await production.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData(1, true, 1)]
    [InlineData(DevClock.MaxOffsetDays, true, DevClock.MaxOffsetDays)]
    [InlineData(0, false, 0)]
    [InlineData(DevClock.MaxOffsetDays + 1, false, 0)]
    public void MovesTheOffsetOnlyInsideTheSupportedRange(int days, bool accepted, int expectedOffset)
    {
        var clock = new DevClock(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());

        bool moved = clock.TryAdvance(days);

        Assert.Equal((accepted, expectedOffset), (moved, clock.OffsetDays));
    }
}
