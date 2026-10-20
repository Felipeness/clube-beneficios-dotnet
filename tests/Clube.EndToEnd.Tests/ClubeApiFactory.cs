using System.Net.Http.Headers;
using System.Text;
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
        builder.UseSetting("Authentication:Schemes:Bearer:ValidIssuer", TokenIssuer);
        builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", TokenAudience);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Issuer", TokenIssuer);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Value", Convert.ToBase64String(SigningKey));
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
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
