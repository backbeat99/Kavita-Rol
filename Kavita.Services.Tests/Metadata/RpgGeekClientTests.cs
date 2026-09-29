using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kavita.API.Services.Metadata;
using Kavita.Services.Metadata;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kavita.Services.Tests.Metadata;

public class RpgGeekClientTests
{
    private const string Token = "secret-test-token";

    private static (RpgGeekClient Client, RecordingHandler Handler) CreateClient(
        string? token,
        IDistributedCache? cache = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["RPGEEK_REQUEST_INTERVAL_SECONDS"] = "0",
            ["RPGEEK_CACHE_SUCCESS_DAYS"] = "7",
            ["RPGEEK_CACHE_EMPTY_MINUTES"] = "15"
        };
        if (token is not null) values["BGG_API_TOKEN"] = token;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var handler = new RecordingHandler();
        return (new RpgGeekClient(new HttpClient(handler), configuration,
            cache ?? new SharedDistributedCache(), NullLogger<RpgGeekClient>.Instance), handler);
    }

    [Fact]
    public async Task Client_is_inactive_without_token_and_never_sends_a_request()
    {
        var (client, handler) = CreateClient(null);

        Assert.False(client.IsEnabled);
        Assert.Empty(await client.SearchProductsAsync("Heart"));
        Assert.Null(await client.GetProductAsync(290374));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Search_parses_only_rpg_items_and_uses_token_header()
    {
        var (client, handler) = CreateClient(Token);
        handler.Enqueue("""
            <?xml version="1.0" encoding="utf-8"?>
            <items total="2">
              <item type="rpgitem" id="290374">
                <name type="primary" value="Heart: The City Beneath"/>
                <yearpublished value="2020"/>
              </item>
              <item type="boardgame" id="999">
                <name type="primary" value="Not an RPG"/>
              </item>
            </items>
            """);

        var result = Assert.Single(await client.SearchProductsAsync("Heart & Spire"));

        Assert.Equal(290374, result.Id);
        Assert.Equal("Heart: The City Beneath", result.Name);
        Assert.Equal(2020, result.YearPublished);
        Assert.Equal($"Bearer {Token}", handler.AuthorizationHeader);
        Assert.DoesNotContain(Token, handler.RequestUri);
        Assert.Contains("Heart & Spire", System.Uri.UnescapeDataString(handler.RequestUri));
    }

    [Fact]
    public async Task Product_parses_year_designers_publishers_and_image()
    {
        var (client, handler) = CreateClient(Token);
        handler.Enqueue("""
            <items>
              <item type="rpgitem" id="370894">
                <name type="primary" value="Frontier Scum"/>
                <yearpublished value="2022"/>
                <description>An acid western RPG.</description>
                <image>https://cf.geekdo-images.com/frontierscum.jpg</image>
                <link type="rpgdesigner" id="11" value="Karl Druid"/>
                <link type="rpgdesigner" id="12" value="Someone Else"/>
                <link type="rpgpublisher" id="21" value="Games Omnivorous"/>
              </item>
            </items>
            """);

        var product = await client.GetProductAsync(370894);

        Assert.NotNull(product);
        Assert.Equal("Frontier Scum", product.Title);
        Assert.Equal(2022, product.YearPublished);
        Assert.Equal("An acid western RPG.", product.Description);
        Assert.Equal("https://cf.geekdo-images.com/frontierscum.jpg", product.ImageUrl);
        Assert.Equal(new[] { "Karl Druid", "Someone Else" }, product.Designers);
        Assert.Equal(new[] { "Games Omnivorous" }, product.Publishers);
    }

    [Fact]
    public async Task Product_decodes_entities_and_removes_markup_from_provider_text()
    {
        var (client, handler) = CreateClient(Token);
        handler.Enqueue("""
            <items>
              <item type="rpgitem" id="370894">
                <name type="primary" value="Frontier Scum &amp;amp; Friends"/>
                <yearpublished value="2022"/>
                <description>You&amp;rsquo;re after the good stuff.&lt;br /&gt;&lt;br /&gt;Faberg&amp;eacute; eggs.</description>
                <link type="rpgdesigner" id="11" value="Ren&amp;eacute; Designer"/>
                <link type="rpgpublisher" id="21" value="Games &amp;amp; Omnivorous"/>
              </item>
            </items>
            """);

        var product = await client.GetProductAsync(370894);

        Assert.NotNull(product);
        Assert.Equal("Frontier Scum & Friends", product.Title);
        Assert.Equal("You’re after the good stuff.\n\nFabergé eggs.", product.Description);
        Assert.Equal(new[] {"René Designer"}, product.Designers);
        Assert.Equal(new[] {"Games & Omnivorous"}, product.Publishers);
    }

    [Fact]
    public async Task Successful_search_is_persisted_across_client_instances_and_normalized_queries_reuse_it()
    {
        var cache = new SharedDistributedCache();
        var (firstClient, firstHandler) = CreateClient(Token, cache);
        firstHandler.Enqueue(SearchResult(370894, "Frontier Scum", 2022));

        var firstResult = await firstClient.SearchProductsAsync("Frontier Scum");
        var (restartedClient, restartedHandler) = CreateClient(Token, cache);
        var afterRestart = await restartedClient.SearchProductsAsync("  frontier   scum  ");

        Assert.Equal(firstResult, afterRestart);
        Assert.Equal(1, firstHandler.RequestCount);
        Assert.Equal(0, restartedHandler.RequestCount);
        Assert.Equal(TimeSpan.FromDays(7), cache.LastExpirationRelativeToNow);
    }

    [Fact]
    public async Task Concurrent_clients_reuse_a_response_already_fetched_for_the_same_query()
    {
        var cache = new SharedDistributedCache();
        var (firstClient, firstHandler) = CreateClient(Token, cache);
        var (secondClient, secondHandler) = CreateClient(Token, cache);
        firstHandler.Enqueue(SearchResult(370894, "Frontier Scum", 2022));
        secondHandler.Enqueue(SearchResult(370894, "Frontier Scum", 2022));

        var results = await Task.WhenAll(
            firstClient.SearchProductsAsync("concurrent cache test"),
            secondClient.SearchProductsAsync("concurrent cache test"));

        Assert.All(results, result => Assert.Equal(370894, Assert.Single(result).Id));
        Assert.Equal(1, firstHandler.RequestCount + secondHandler.RequestCount);
    }

    [Fact]
    public async Task Empty_search_is_cached_only_for_the_short_negative_ttl()
    {
        var cache = new SharedDistributedCache();
        var (client, handler) = CreateClient(Token, cache);
        handler.Enqueue("<items total=\"0\"/>");
        handler.Enqueue(SearchResult(370894, "Frontier Scum", 2022));

        Assert.Empty(await client.SearchProductsAsync("Missing RPG"));
        Assert.Empty(await client.SearchProductsAsync("Missing RPG"));
        Assert.Equal(TimeSpan.FromMinutes(15), cache.LastExpirationRelativeToNow);

        cache.Advance(TimeSpan.FromMinutes(16));
        Assert.Single(await client.SearchProductsAsync("Missing RPG"));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Forced_search_bypasses_cache_and_replaces_it_with_the_new_success()
    {
        var (client, handler) = CreateClient(Token);
        handler.Enqueue(SearchResult(1, "Old result", 2020));
        handler.Enqueue(SearchResult(2, "Updated result", 2024));

        Assert.Equal(1, Assert.Single(await client.SearchProductsAsync("same query")).Id);
        Assert.Equal(2, Assert.Single(await client.SearchProductsAsync("same query", forceRefresh: true)).Id);
        Assert.Equal(2, Assert.Single(await client.SearchProductsAsync("same query")).Id);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Failed_request_is_not_cached_and_a_later_retry_reaches_the_provider()
    {
        var (client, handler) = CreateClient(Token);
        handler.Enqueue("rate limited", HttpStatusCode.TooManyRequests);
        handler.Enqueue(SearchResult(370894, "Frontier Scum", 2022));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchProductsAsync("Frontier Scum"));
        var result = Assert.Single(await client.SearchProductsAsync("Frontier Scum"));

        Assert.Equal(370894, result.Id);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Invalid_xml_is_not_cached()
    {
        var (client, handler) = CreateClient(Token);
        handler.Enqueue("<items>");
        handler.Enqueue(SearchResult(370894, "Frontier Scum", 2022));

        await Assert.ThrowsAsync<System.Xml.XmlException>(() => client.SearchProductsAsync("Frontier Scum"));
        Assert.Equal(370894, Assert.Single(await client.SearchProductsAsync("Frontier Scum")).Id);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Product_details_are_persisted_and_forced_refresh_replaces_the_cached_value()
    {
        var cache = new SharedDistributedCache();
        var (firstClient, firstHandler) = CreateClient(Token, cache);
        firstHandler.Enqueue(ProductResult(370894, "Old title"));
        var original = await firstClient.GetProductAsync(370894);
        Assert.Equal("Old title", original?.Title);

        var (refreshClient, refreshHandler) = CreateClient(Token, cache);
        refreshHandler.Enqueue(ProductResult(370894, "Current title"));
        var refreshed = await refreshClient.GetProductAsync(370894, forceRefresh: true);
        Assert.Equal("Current title", refreshed?.Title);

        var (laterClient, laterHandler) = CreateClient(Token, cache);
        Assert.Equal("Current title", (await laterClient.GetProductAsync(370894))?.Title);
        Assert.Equal(1, firstHandler.RequestCount);
        Assert.Equal(1, refreshHandler.RequestCount);
        Assert.Equal(0, laterHandler.RequestCount);
        Assert.Equal(TimeSpan.FromDays(7), cache.LastExpirationRelativeToNow);
    }

    [Fact]
    public async Task Missing_product_is_short_cached_but_expired_missing_entry_is_retried()
    {
        var cache = new SharedDistributedCache();
        var (client, handler) = CreateClient(Token, cache);
        handler.Enqueue("<items total=\"0\"/>");
        handler.Enqueue(ProductResult(290374, "Heart: The City Beneath"));

        Assert.Null(await client.GetProductAsync(290374));
        Assert.Null(await client.GetProductAsync(290374));
        Assert.Equal(TimeSpan.FromMinutes(15), cache.LastExpirationRelativeToNow);
        cache.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal("Heart: The City Beneath", (await client.GetProductAsync(290374))?.Title);
        Assert.Equal(2, handler.RequestCount);
    }

    private static string SearchResult(int id, string name, int year) =>
        $"<items total=\"1\"><item type=\"rpgitem\" id=\"{id}\"><name type=\"primary\" value=\"{name}\"/><yearpublished value=\"{year}\"/></item></items>";

    private static string ProductResult(int id, string title) =>
        $"<items><item type=\"rpgitem\" id=\"{id}\"><name type=\"primary\" value=\"{title}\"/><yearpublished value=\"2022\"/></item></items>";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Response)> _responses = new();

        public int RequestCount { get; private set; }
        public string? AuthorizationHeader { get; private set; }
        public string RequestUri { get; private set; } = string.Empty;

        public void Enqueue(string response, HttpStatusCode statusCode = HttpStatusCode.OK) =>
            _responses.Enqueue((statusCode, response));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            var (statusCode, body) = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.OK, "<items/>");
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
        }
    }

    private sealed class SharedDistributedCache : IDistributedCache
    {
        private sealed record CacheEntry(byte[] Data, DateTimeOffset? ExpiresAtUtc);

        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public TimeSpan? LastExpirationRelativeToNow { get; private set; }

        public void Advance(TimeSpan duration) => _now += duration;

        public byte[]? Get(string key) => GetCore(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(GetCore(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _entries.TryRemove(key, out _);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => SetCore(key, value, options);
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            SetCore(key, value, options);
            return Task.CompletedTask;
        }

        private byte[]? GetCore(string key)
        {
            if (!_entries.TryGetValue(key, out var entry)) return null;
            if (entry.ExpiresAtUtc.HasValue && entry.ExpiresAtUtc.Value <= _now)
            {
                _entries.TryRemove(key, out _);
                return null;
            }

            return entry.Data.ToArray();
        }

        private void SetCore(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            LastExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;
            var expiresAt = options.AbsoluteExpiration ??
                            (options.AbsoluteExpirationRelativeToNow.HasValue
                                ? _now.Add(options.AbsoluteExpirationRelativeToNow.Value)
                                : null);
            _entries[key] = new CacheEntry(value.ToArray(), expiresAt);
        }
    }
}
