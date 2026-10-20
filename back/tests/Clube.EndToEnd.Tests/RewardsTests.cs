using System.Net;
using System.Net.Http.Json;
using Clube.Core.Ports.Driving;

namespace Clube.EndToEnd.Tests;

public class RewardsTests
{
    [Fact]
    public async Task ListsTheCatalogWithoutATokenAndWithTheFinalPrices()
    {
        await using var factory = new ClubeApiFactory();

        var response = await factory.CreateClient().GetAsync("/rewards");
        var rewards = await response.Content.ReadFromJsonAsync<RewardView[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(rewards);
        Assert.Equal(["cinema-ticket", "perfume-kit", "thermal-bottle"], rewards.Select(reward => reward.Id));
        var bottle = rewards.Single(reward => reward.Id == "thermal-bottle");
        Assert.Equal((2990L, 15m, 2541L), (bottle.ListPriceCents, bottle.DiscountPercent, bottle.PriceCents));
        Assert.All(rewards.Where(reward => reward.Id != "thermal-bottle"), reward => Assert.Equal((0m, reward.ListPriceCents), (reward.DiscountPercent, reward.PriceCents)));
    }

    [Fact]
    public async Task DocumentsTheCatalogAsAnOpenRoute()
    {
        await using var factory = new ClubeApiFactory();

        string document = await factory.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"/rewards\"", document, StringComparison.Ordinal);
        Assert.Contains("\"/dev/tokens\"", document, StringComparison.Ordinal);
    }
}
