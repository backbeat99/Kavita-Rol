using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kavita.Services.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kavita.Services.Tests.Metadata;

public class RpgGeekClientTests
{
    private const string Token = "test-token";

    private static (RpgGeekClient client, CountingHandler handler) CreateClient(string? token)
    {
        var values = new Dictionary<string, string?>();
        if (token != null) values["BGG_API_TOKEN"] = token;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var handler = new CountingHandler();
        var httpClient = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new RpgGeekClient(httpClient, configuration, cache), handler);
    }

    [Fact]
    public async Task Client_is_inactive_without_token()
    {
        var (client, handler) = CreateClient(null);

        var results = await client.SearchProductsAsync("Into the Odd");
        var product = await client.GetProductAsync(290374);

        Assert.False(client.IsEnabled);
        Assert.Empty(results);
        Assert.Null(product);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Search_parses_rpg_items_and_skips_other_types()
    {
        var (client, handler) = CreateClient(Token);
        handler.Response = """
            <?xml version="1.0" encoding="utf-8"?>
            <items total="2">
              <item type="rpgitem" id="290374">
                <name type="primary" value="Heart: The City Beneath"/>
                <yearpublished value="2020"/>
              </item>
              <item type="boardgame" id="999">
                <name type="primary" value="Not an RPG"/>
                <yearpublished value="2010"/>
              </item>
            </items>
            """;

        var results = await client.SearchProductsAsync("Heart");

        var result = Assert.Single(results);
        Assert.Equal(290374, result.Id);
        Assert.Equal("Heart: The City Beneath", result.Name);
        Assert.Equal(2020, result.YearPublished);
    }

    [Fact]
    public async Task Product_parses_bibliographic_fields()
    {
        var (client, handler) = CreateClient(Token);
        handler.Response = """
            <?xml version="1.0" encoding="utf-8"?>
            <items termsofuse="https://boardgamegeek.com/xmlapi/termsofuse">
              <item type="rpgitem" id="370894">
                <name type="primary" value="Frontier Scum"/>
                <yearpublished value="2022"/>
                <description>An acid western RPG.</description>
                <image>https://cf.geekdo-images.com/frontierscum.jpg</image>
                <link type="rpgdesigner" id="11" value="Karl Druid"/>
                <link type="rpgdesigner" id="12" value="Someone Else"/>
                <link type="rpgpublisher" id="21" value="Games Omnivorous"/>
                <link type="rpggenre" id="31" value="Western"/>
              </item>
            </items>
            """;

        var product = await client.GetProductAsync(370894);

        Assert.NotNull(product);
        Assert.Equal("Frontier Scum", product.Title);
        Assert.Equal(2022, product.YearPublished);
        Assert.Equal("An acid western RPG.", product.Description);
        Assert.Equal("https://cf.geekdo-images.com/frontierscum.jpg", product.ImageUrl);
        Assert.Equal(["Karl Druid", "Someone Else"], product.Designers);
        Assert.Equal(["Games Omnivorous"], product.Publishers);
    }

    [Fact]
    public async Task Search_results_are_cached_and_not_repeated()
    {
        var (client, handler) = CreateClient(Token);
        handler.Response = """
            <?xml version="1.0" encoding="utf-8"?>
            <items total="1">
              <item type="rpgitem" id="290374">
                <name type="primary" value="Heart: The City Beneath"/>
                <yearpublished value="2020"/>
              </item>
            </items>
            """;

        await client.SearchProductsAsync("Heart");
        await client.SearchProductsAsync("Heart");
        await client.SearchProductsAsync("heart");

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Product_results_are_cached_per_id()
    {
        var (client, handler) = CreateClient(Token);
        handler.Response = """
            <?xml version="1.0" encoding="utf-8"?>
            <items termsofuse="https://boardgamegeek.com/xmlapi/termsofuse">
              <item type="rpgitem" id="370894">
                <name type="primary" value="Frontier Scum"/>
                <yearpublished value="2022"/>
              </item>
            </items>
            """;

        await client.GetProductAsync(370894);
        await client.GetProductAsync(370894);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Missing_product_returns_null_and_briefly_caches_the_miss()
    {
        var (client, handler) = CreateClient(Token);
        handler.Response = """
            <?xml version="1.0" encoding="utf-8"?>
            <items termsofuse="https://boardgamegeek.com/xmlapi/termsofuse"/>
            """;

        Assert.Null(await client.GetProductAsync(123));
        Assert.Null(await client.GetProductAsync(123));
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string Response { get; set; } = "<items/>";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Response)
            });
        }
    }
}
