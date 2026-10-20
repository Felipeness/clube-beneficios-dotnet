using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Hosting;

namespace Clube.EndToEnd.Tests;

public class AuthorizationTests
{
    public enum Caller { NoToken, Owner, OtherMember, Operator, ForeignKey, Expired }

    public enum Route { Get, Statement, Redeem, Reverse }

    public static TheoryData<Caller, Route, HttpStatusCode> Access => new()
    {
        { Caller.NoToken, Route.Get, HttpStatusCode.Unauthorized },
        { Caller.NoToken, Route.Reverse, HttpStatusCode.Unauthorized },
        { Caller.ForeignKey, Route.Get, HttpStatusCode.Unauthorized },
        { Caller.Expired, Route.Statement, HttpStatusCode.Unauthorized },
        { Caller.Owner, Route.Get, HttpStatusCode.OK },
        { Caller.Owner, Route.Statement, HttpStatusCode.OK },
        { Caller.Owner, Route.Redeem, HttpStatusCode.OK },
        { Caller.Owner, Route.Reverse, HttpStatusCode.Forbidden },
        { Caller.OtherMember, Route.Get, HttpStatusCode.Forbidden },
        { Caller.OtherMember, Route.Statement, HttpStatusCode.Forbidden },
        { Caller.OtherMember, Route.Redeem, HttpStatusCode.Forbidden },
        { Caller.Operator, Route.Get, HttpStatusCode.OK },
        { Caller.Operator, Route.Statement, HttpStatusCode.OK },
        { Caller.Operator, Route.Redeem, HttpStatusCode.Forbidden },
        { Caller.Operator, Route.Reverse, HttpStatusCode.OK },
    };

    [Theory]
    [MemberData(nameof(Access))]
    public async Task GrantsEachRouteOnlyToItsCallers(Caller caller, Route route, HttpStatusCode expected)
    {
        await using var factory = new ClubeApiFactory();
        using var anonymous = factory.CreateClient();
        Guid owner = await SubscribeAsync(anonymous, "529.982.247-25");
        Guid stranger = await SubscribeAsync(anonymous, "111.444.777-35");
        await PixWebhook.CreditAsync(factory, anonymous, owner, "2026-10");
        Guid credit = CreditIdOf(factory);
        using var client = ClientFor(factory, caller, owner, stranger);

        var response = await client.SendAsync(RequestFor(route, owner, credit));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task StampsTheTokenHoldersOnTheLedger()
    {
        await using var factory = new ClubeApiFactory();
        using var anonymous = factory.CreateClient();
        Guid owner = await SubscribeAsync(anonymous, "529.982.247-25");
        await PixWebhook.CreditAsync(factory, anonymous, owner, "2026-10");
        using var member = factory.CreateClientAsMember(owner);
        using var staff = factory.CreateClientAsOperator();

        Assert.Equal(HttpStatusCode.OK, (await member.SendAsync(RequestFor(Route.Redeem, owner, Guid.Empty))).StatusCode);
        Guid credit = CreditIdOf(factory);
        Assert.Equal(HttpStatusCode.OK, (await staff.SendAsync(RequestFor(Route.Reverse, owner, credit))).StatusCode);

        Assert.Equal($"Member:{owner}", factory.QueryScalar("SELECT ActorKind || ':' || ActorId FROM ledger_transactions WHERE Description LIKE 'Redemption%'"));
        Assert.Equal("Operator:tester-1", factory.QueryScalar("SELECT ActorKind || ':' || ActorId FROM ledger_transactions WHERE Description LIKE 'Reversal%'"));
    }

    // The webhook proves itself with its HMAC; a stray or broken bearer header must neither block it nor name the actor.
    [Fact]
    public async Task AcceptsTheSignedWebhookWhateverBearerItCarries()
    {
        await using var factory = new ClubeApiFactory();
        using var client = factory.CreateClientWith("not-a-jwt");
        using var anonymous = factory.CreateClient();
        Guid owner = await SubscribeAsync(anonymous, "529.982.247-25");

        await PixWebhook.CreditAsync(factory, client, owner, "2026-10");

        Assert.Equal("PaymentProvider", factory.QueryScalar("SELECT ActorKind FROM ledger_transactions"));
    }

    [Fact]
    public async Task DocumentsTheBearerSchemeAndItsRejectionsInOpenApi()
    {
        await using var factory = new ClubeApiFactory();
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        JsonElement root = document.RootElement;
        JsonElement statement = root.GetProperty("paths").GetProperty("/memberships/{id}/statement").GetProperty("get");
        JsonElement subscribe = root.GetProperty("paths").GetProperty("/memberships").GetProperty("post");

        Assert.Equal("bearer", root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.True(statement.TryGetProperty("security", out _), "protected routes must ask for the bearer token");
        Assert.True(statement.GetProperty("responses").TryGetProperty("401", out _), "protected routes must document 401");
        Assert.True(statement.GetProperty("responses").TryGetProperty("403", out _), "protected routes must document 403");
        Assert.False(subscribe.TryGetProperty("security", out _), "sign-up stays open");
    }

    [Fact]
    public async Task RefusesToBootOutsideDevelopmentWithoutATrustedIssuer()
    {
        await using var factory = new ClubeApiFactory();
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production").UseSetting("Authentication:Schemes:Bearer:ValidIssuer", ""));

        var refused = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains("Authentication:Schemes:Bearer", refused.ToString());
    }

    private static HttpClient ClientFor(ClubeApiFactory factory, Caller caller, Guid owner, Guid stranger) => caller switch
    {
        Caller.NoToken => factory.CreateClient(),
        Caller.Owner => factory.CreateClientAsMember(owner),
        Caller.OtherMember => factory.CreateClientAsMember(stranger),
        Caller.Operator => factory.CreateClientAsOperator(),
        Caller.ForeignKey => factory.CreateClientWith(ClubeApiFactory.Token(OwnerClaims(owner), key: Encoding.UTF8.GetBytes("another-signing-key-0123456789abcdefgh"))),
        Caller.Expired => factory.CreateClientWith(ClubeApiFactory.Token(OwnerClaims(owner), expires: DateTime.UtcNow.AddMinutes(-10))),
        _ => throw new ArgumentOutOfRangeException(nameof(caller)),
    };

    private static Dictionary<string, object> OwnerClaims(Guid owner) => new() { ["membership_id"] = owner.ToString() };

    private static HttpRequestMessage RequestFor(Route route, Guid membership, Guid transaction) => route switch
    {
        Route.Get => new HttpRequestMessage(HttpMethod.Get, $"/memberships/{membership}"),
        Route.Statement => new HttpRequestMessage(HttpMethod.Get, $"/memberships/{membership}/statement"),
        Route.Redeem => new HttpRequestMessage(HttpMethod.Post, $"/memberships/{membership}/redemptions")
        {
            Content = JsonContent.Create(new { rewardId = "cinema-ticket" }),
            Headers = { { "Idempotency-Key", "order-1" } },
        },
        Route.Reverse => new HttpRequestMessage(HttpMethod.Post, $"/memberships/{membership}/transactions/{transaction}/reversal"),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private static async Task<Guid> SubscribeAsync(HttpClient client, string cpf)
    {
        var response = await client.PostAsJsonAsync("/memberships", new { cpf });
        var created = await response.Content.ReadFromJsonAsync<MembershipView>();
        Assert.NotNull(created);
        return created.Id;
    }

    private static Guid CreditIdOf(ClubeApiFactory factory)
    {
        string? id = factory.QueryScalar("SELECT Id FROM ledger_transactions WHERE Description LIKE 'Monthly allowance%'");
        Assert.NotNull(id);
        return Guid.Parse(id);
    }
}
