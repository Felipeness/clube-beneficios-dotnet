using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clube.Core.Ports.Driving;

namespace Clube.EndToEnd.Tests;

public class MembershipApiTests
{
    private const string ValidCpf = "529.982.247-25";

    [Fact]
    public async Task CreditsAllowanceIdempotentlyAndRedeemsReward()
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var created = await SubscribeAsync(client);

        await PostAsync(client, $"/memberships/{created.Id}/allowance-credits", new { period = "2026-10" });
        var retried = await PostAsync(client, $"/memberships/{created.Id}/allowance-credits", new { period = "2026-10" });
        var redeemed = await PostAsync(client, $"/memberships/{created.Id}/redemptions", new { rewardId = "cinema-ticket" });
        var fetched = await client.GetFromJsonAsync<MembershipView>($"/memberships/{created.Id}");

        Assert.Equal("***.982.247-**", created.Holder);
        Assert.Equal(20, retried.Balance);
        Assert.Equal(10, redeemed.Balance);
        Assert.Equal(10, fetched?.Balance);
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

    public static TheoryData<string, object, HttpStatusCode> InvalidRequests => new()
    {
        { "/memberships", new { cpf = "111.111.111-11" }, HttpStatusCode.BadRequest },
        { "/memberships", new { cpf = (string?)null }, HttpStatusCode.BadRequest },
        { "/memberships", new { }, HttpStatusCode.BadRequest },
        { $"/memberships/{Guid.Empty}/redemptions", new { rewardId = "cinema-ticket" }, HttpStatusCode.NotFound },
        { "{membership}/redemptions", new { rewardId = "yacht" }, HttpStatusCode.NotFound },
        { "{membership}/redemptions", new { rewardId = "cinema-ticket" }, HttpStatusCode.UnprocessableEntity },
        { "{membership}/allowance-credits", new { period = "2026-13" }, HttpStatusCode.BadRequest },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task MapsRejectionsToProblemStatus(string path, object body, HttpStatusCode expected)
    {
        await using var factory = new ClubeApiFactory();
        var client = factory.CreateClient();
        var membership = await SubscribeAsync(client);

        var response = await client.PostAsJsonAsync(path.Replace("{membership}", $"/memberships/{membership.Id}"), body);

        Assert.Equal(expected, response.StatusCode);
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

    private static async Task<MembershipView> SubscribeAsync(HttpClient client) =>
        await PostAsync(client, "/memberships", new { cpf = ValidCpf }, HttpStatusCode.Created);

    private static async Task<MembershipView> PostAsync(
        HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync(path, body);
        Assert.Equal(expected, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<MembershipView>();
        Assert.NotNull(view);
        return view;
    }
}
