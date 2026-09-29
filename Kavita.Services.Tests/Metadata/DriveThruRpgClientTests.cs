using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kavita.API.Services.Metadata;
using Kavita.Services.Metadata;

namespace Kavita.Services.Tests.Metadata;

public class DriveThruRpgClientTests
{
    [Fact]
    public async Task Search_parses_only_products_and_encodes_query()
    {
        HttpRequestMessage? request = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(message =>
        {
            request = message;
            return JsonResponse("""
                {"data":[
                  {"attributes":{"entityType":"Product","entityId":12345,"name":"The Great Book of Random Tables"}},
                  {"attributes":{"entityId":0,"name":"Invalid"}},
                  {"attributes":{"entityType":"Category","entityId":678,"name":"Category result"}}
                ]}
                """);
        }));
        var client = new DriveThruRpgClient(httpClient);

        var results = await client.SearchProductsAsync("The Great Book & Tables");

        var result = Assert.Single(results);
        Assert.Equal(12345, result.ProductId);
        Assert.Equal("The Great Book of Random Tables", result.Title);
        Assert.Contains("keyword=The%20Great%20Book%20%26%20Tables", request!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("groupId=29", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("siteId=29", request.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_details_map_public_bibliographic_fields()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "data": {
                "id": 2468,
                "attributes": {
                  "description": {"name": "Dragonbane Core Rulebook", "description": "<p>Core rules for &eacute;lite play.</p>"},
                  "authors": ["Author One", {"name":"Author Two"}],
                  "dateAvailable": "2024-01-02T23:15:00-05:00",
                  "image": "8957/240640.jpg",
                  "filters": [{"attributes":{"parentId":40,"name":"English"}}]
                }
              },
              "included": [{"type":"Publisher","attributes":{"name":"Publisher One"}}]
            }
            """)));
        var client = new DriveThruRpgClient(httpClient);

        var product = await client.GetProductAsync(2468);

        Assert.NotNull(product);
        Assert.Equal(2468, product.ProductId);
        Assert.Equal("Dragonbane Core Rulebook", product.Title);
        Assert.Equal(new[] { "Author One", "Author Two" }, product.Authors);
        Assert.Equal("Core rules for élite play.", product.Description);
        Assert.Equal("Publisher One", product.Publisher);
        Assert.Equal(new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), product.ReleaseDate);
        Assert.Equal("https://www.drivethrurpg.com/images/8957/240640.jpg", product.CoverUrl);
        Assert.Equal("en", product.LanguageCode);
    }

    [Fact]
    public async Task Unknown_language_is_not_inferred()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "data":{"attributes":{"description":{"name":"A title"},"filters":[{"attributes":{"parentId":40,"name":"Unknown language"}}]}},
              "included":[{"type":"Category","attributes":{"parentId":40,"name":"Art"}}]
            }
            """)));
        var client = new DriveThruRpgClient(httpClient);

        var product = await client.GetProductAsync(1357);

        Assert.NotNull(product);
        Assert.Null(product.LanguageCode);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
