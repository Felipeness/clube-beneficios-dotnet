using System.Net.Http.Headers;
using System.Text;
using Clube.Adapters.DevTools;
using Clube.Adapters.Jobs;
using Clube.Adapters.Partners;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Vouchers;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Clube.EndToEnd.Tests;

// Each factory gets its own in-memory SQLite database, kept alive by one open connection
// because SQLite drops a shared in-memory database when its last connection closes.
public sealed class ClubeApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString = $"Data Source=clube-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection keepAlive;

    public ClubeApiFactory()
    {
        keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
    }

    // Talks to the database behind the API's back, to prove rules that live in the schema itself.
    public int ExecuteSql(string sql)
    {
        using var command = keepAlive.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    public string? QueryScalar(string sql)
    {
        using var command = keepAlive.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
    }

    // The webhook signs with this; it is a test value, not the Development one, so tests never depend on appsettings.
    public const string WebhookSecret = "test-pix-webhook-secret-0123456789abcdef";

    // Test-only PII keys, distinct from Development's, so a test never reads a value another environment wrote.
    public static readonly string PiiEncryptionKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-pii-encryption-key-32-bytes"));
    public static readonly string PiiIndexKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-pii-blind-index-key-32bytes"));

    // Plays the identity provider: the API trusts tokens signed with this key for this issuer and audience, configured
    // through the same Authentication:Schemes:Bearer keys dotnet user-jwts writes.
    public const string TokenIssuer = "clube-tests";
    public const string TokenAudience = "clube-api";
    public static readonly byte[] SigningKey = Encoding.UTF8.GetBytes("test-jwt-signing-key-0123456789abcdef");

    // The ledger rules depend on "now", so tests own the clock instead of reading the machine's.
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Clube", connectionString);
        builder.UseSetting("Psp:WebhookSecret", WebhookSecret);
        builder.UseSetting("Pii:EncryptionKey", PiiEncryptionKey);
        builder.UseSetting("Pii:IndexKey", PiiIndexKey);
        builder.UseSetting("Authentication:Schemes:Bearer:ValidIssuer", TokenIssuer);
        builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", TokenAudience);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Issuer", TokenIssuer);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Value", Convert.ToBase64String(SigningKey));
        builder.ConfigureTestServices(services =>
        {
            // The dev clock sits on top of the fake: offset 0 changes nothing, and /dev/clock moves it for the tests that ask.
            services.AddSingleton(new DevClock(Clock));
            services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<DevClock>());
            // A relay running on its own would race the assertions; tests settle the outbox with DispatchOutboxAsync instead.
            services.Remove(services.Single(service => service.ImplementationType == typeof(OutboxWorker)));
            // Same for credit expiry: a daily run at startup would expire under the tests' feet; RunCreditExpiryAsync triggers it.
            services.Remove(services.Single(service => service.ImplementationType == typeof(CreditExpiryWorker)));
            // Reconciliation reads the PSP's statement: tests feed it through Psp and ask for a day with the route.
            services.Remove(services.Single(service => service.ImplementationType == typeof(ReconciliationWorker)));
        });
    }

    // A non-Development host never migrates, so a test that boots one needs the schema applied first by a Development boot.
    public void MigrateDatabase() => _ = Services;

    public ConfigurablePspSettlements Psp => Services.GetRequiredService<ConfigurablePspSettlements>();

    public async Task<IReadOnlyList<OutboxMessage>> DispatchOutboxAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IForRelayingTheOutbox>().DispatchBatchAsync(default);
    }

    public async Task<CreditExpiryReport> RunCreditExpiryAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IForExpiringCredits>().RunAsync(CreditExpiry.CutoffAt(Clock.GetUtcNow()), default);
    }

    public HttpClient CreateClientAsMember(Guid membershipId) => CreateClientWith(MemberToken(membershipId));

    public HttpClient CreateClientAsOperator() => CreateClientWith(OperatorToken());

    public HttpClient CreateClientWith(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static string MemberToken(Guid membershipId) => Token(new Dictionary<string, object> { ["membership_id"] = membershipId.ToString() });

    public static string OperatorToken() => Token(new Dictionary<string, object> { ["role"] = "operator" });

    // Token lifetimes are checked against the real clock by the JWT handler, not the fake one the ledger uses.
    public static string Token(Dictionary<string, object> claims, DateTime? expires = null, byte[]? key = null)
    {
        DateTime expiresAt = expires ?? DateTime.UtcNow.AddMinutes(10);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TokenIssuer,
            Audience = TokenAudience,
            Claims = new Dictionary<string, object>(claims) { ["sub"] = "tester-1" },
            IssuedAt = expiresAt.AddMinutes(-20),
            NotBefore = expiresAt.AddMinutes(-20),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key ?? SigningKey), SecurityAlgorithms.HmacSha256),
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) keepAlive.Dispose();
        base.Dispose(disposing);
    }
}
