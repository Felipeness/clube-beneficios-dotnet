using System.Net;
using System.Net.Http.Json;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Hosting;

namespace Clube.EndToEnd.Tests;

// Reads the memberships table behind the API's back: what matters is what a database dump would show.
public class PiiProtectionTests
{
    private const string FormattedCpf = "529.982.247-25";
    private const string CpfDigits = "52998224725";

    [Fact]
    public async Task StoresTheHolderEncryptedWithAFreshNonceEachTime()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();

        await SubscribeAsync(client, FormattedCpf);
        string? first = factory.QueryScalar("SELECT Holder FROM memberships");
        string? firstIndex = factory.QueryScalar("SELECT HolderIndex FROM memberships");
        factory.ExecuteSql("DELETE FROM memberships");
        await SubscribeAsync(client, FormattedCpf);
        string? second = factory.QueryScalar("SELECT Holder FROM memberships");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(firstIndex);
        Assert.StartsWith("v1:", first);
        Assert.DoesNotContain(CpfDigits, first);
        Assert.DoesNotContain("982.247", first);
        Assert.NotEqual(first, second);
        Assert.Equal(firstIndex, factory.QueryScalar("SELECT HolderIndex FROM memberships"));
        Assert.DoesNotContain(CpfDigits, firstIndex);
    }

    // The unique index sits on the HMAC of the digits, so formatting does not open a second membership.
    [Theory]
    [InlineData(FormattedCpf, FormattedCpf)]
    [InlineData(FormattedCpf, CpfDigits)]
    public async Task RejectsTheSameCpfThroughTheBlindIndex(string first, string second)
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        await SubscribeAsync(client, first);

        var response = await client.PostAsJsonAsync("/memberships", new { cpf = second });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("1", factory.QueryScalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = 'memberships' AND sql LIKE '%UNIQUE%HolderIndex%'"));
    }

    [Fact]
    public async Task ReadsTheHolderBackDecryptedAndMasked()
    {
        await using var factory = new ClubeApiFactory();
        var created = await SubscribeAsync(factory.CreateClient(), FormattedCpf);
        using var member = factory.CreateClientAsMember(created.Id);

        var read = await member.GetFromJsonAsync<MembershipView>($"/memberships/{created.Id}");

        Assert.NotNull(read);
        Assert.Equal("***.982.247-**", read.Holder);
    }

    [Fact]
    public async Task FailsLoudlyWhenTheStoredCiphertextWasTamperedWith()
    {
        await using var factory = new ClubeApiFactory();
        var created = await SubscribeAsync(factory.CreateClient(), FormattedCpf);
        using var member = factory.CreateClientAsMember(created.Id);
        string stored = factory.QueryScalar("SELECT Holder FROM memberships") ?? "";
        byte[] payload = Convert.FromBase64String(stored["v1:".Length..]);
        payload[14] ^= 0x01;
        factory.ExecuteSql($"UPDATE memberships SET Holder = 'v1:{Convert.ToBase64String(payload)}'");

        var response = await member.GetAsync($"/memberships/{created.Id}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    public static TheoryData<string, string> InvalidKeys => new()
    {
        { "", ClubeApiFactory.PiiIndexKey },
        { ClubeApiFactory.PiiEncryptionKey, "" },
        { Convert.ToBase64String(new byte[16]), ClubeApiFactory.PiiIndexKey },
        { "not base64 at all", ClubeApiFactory.PiiIndexKey },
        { ClubeApiFactory.PiiIndexKey, ClubeApiFactory.PiiIndexKey },
    };

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public async Task RefusesToBootInProductionWithoutTwoValidKeys(string encryptionKey, string indexKey)
    {
        await using var factory = new ClubeApiFactory();
        using var production = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("Pii:EncryptionKey", encryptionKey)
            .UseSetting("Pii:IndexKey", indexKey));

        var refused = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains("Pii:EncryptionKey", refused.ToString());
    }

    private static async Task<MembershipView> SubscribeAsync(HttpClient client, string cpf)
    {
        var response = await client.PostAsJsonAsync("/memberships", new { cpf });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<MembershipView>();
        Assert.NotNull(created);
        return created;
    }
}
