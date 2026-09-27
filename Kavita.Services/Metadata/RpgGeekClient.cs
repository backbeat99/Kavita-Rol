using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kavita.API.Services.Metadata;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kavita.Services.Metadata;

/// <summary>
/// Read-only RPGGeek XML API client. Successful responses are persisted through the configured
/// distributed cache; transient failures are never stored, and explicit refreshes bypass cached data.
/// </summary>
public sealed class RpgGeekClient(
    HttpClient httpClient,
    IConfiguration configuration,
    IDistributedCache cache,
    ILogger<RpgGeekClient> logger) : IRpgGeekClient
{
    private const string ApiRoot = "https://api.geekdo.com/xmlapi2";
    private const string UserAgent = "Kavita RPG catalog metadata client";
    private const string CachePrefix = "rpggeek:v1:";
    private static readonly SemaphoreSlim RateGate = new(1, 1);
    private static readonly SemaphoreSlim[] ResponseCacheGates = Enumerable.Range(0, 128)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);
    private static DateTime _lastRequestUtc = DateTime.MinValue;
    private static DateTime _retryAfterUtc = DateTime.MinValue;

    private readonly string? _apiToken = NormalizeToken(configuration["BGG_API_TOKEN"]);
    private readonly TimeSpan _requestSpacing = ReadRequestSpacing(configuration["RPGEEK_REQUEST_INTERVAL_SECONDS"]);
    private readonly TimeSpan _successCacheDuration = ReadDuration(
        configuration["RPGEEK_CACHE_SUCCESS_DAYS"], TimeSpan.FromDays(7), TimeSpan.FromDays(365), TimeSpan.FromDays(1));
    private readonly TimeSpan _emptyCacheDuration = ReadDuration(
        configuration["RPGEEK_CACHE_EMPTY_MINUTES"], TimeSpan.FromMinutes(15), TimeSpan.FromDays(1), TimeSpan.FromMinutes(1));

    public bool IsEnabled => _apiToken is not null;

    public async Task<IReadOnlyList<RpgGeekSearchResult>> SearchProductsAsync(
        string query,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(query)) return [];

        var cacheKey = SearchCacheKey(query);
        var gate = GetResponseCacheGate(cacheKey);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh)
            {
                var (found, cached) = await TryReadCacheAsync<RpgGeekSearchResult[]>(cacheKey, cancellationToken);
                if (found && cached is not null) return cached;
            }

            var xml = await GetXmlAsync(
                $"{ApiRoot}/search?query={Uri.EscapeDataString(query.Trim())}&type=rpgitem",
                cancellationToken);
            var results = ParseSearch(xml).ToArray();
            await TryWriteCacheAsync(cacheKey, results,
                results.Length == 0 ? _emptyCacheDuration : _successCacheDuration, cancellationToken);
            return results;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RpgGeekProduct?> GetProductAsync(
        int id,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        if (!IsEnabled || id <= 0) return null;

        var cacheKey = ProductCacheKey(id);
        var gate = GetResponseCacheGate(cacheKey);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh)
            {
                var (found, cached) = await TryReadCacheAsync<ProductCacheEnvelope>(cacheKey, cancellationToken);
                if (found && cached is not null) return cached.Product;
            }

            var xml = await GetXmlAsync($"{ApiRoot}/thing?id={id}&type=rpgitem", cancellationToken);
            var product = ParseProduct(xml, id);
            await TryWriteCacheAsync(cacheKey, new ProductCacheEnvelope(product),
                product is null ? _emptyCacheDuration : _successCacheDuration, cancellationToken);
            return product;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<string> GetXmlAsync(string url, CancellationToken cancellationToken)
    {
        await RateGate.WaitAsync(cancellationToken);
        try
        {
            var waitForSpacing = _requestSpacing - (DateTime.UtcNow - _lastRequestUtc);
            var waitForRetryAfter = _retryAfterUtc - DateTime.UtcNow;
            var wait = waitForSpacing > waitForRetryAfter ? waitForSpacing : waitForRetryAfter;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiToken}");
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            _lastRequestUtc = DateTime.UtcNow;
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("RPGGeek returned HTTP {StatusCode} for a catalog request", (int)response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retryAfter)
            {
                _retryAfterUtc = retryAfter.Date?.UtcDateTime ??
                                 (retryAfter.Delta.HasValue ? DateTime.UtcNow.Add(retryAfter.Delta.Value) : DateTime.MinValue);
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        finally
        {
            RateGate.Release();
        }
    }

    private async Task<(bool Found, T? Value)> TryReadCacheAsync<T>(string key, CancellationToken cancellationToken)
        where T : class
    {
        byte[]? bytes;
        try
        {
            bytes = await cache.GetAsync(key, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("RPGGeek response cache read failed ({ErrorType}); querying the provider", exception.GetType().Name);
            return (false, null);
        }

        if (bytes is null) return (false, null);
        try
        {
            var value = JsonSerializer.Deserialize<T>(bytes, CacheJsonOptions);
            if (value is not null) return (true, value);
        }
        catch (JsonException exception)
        {
            logger.LogWarning("Ignoring an invalid RPGGeek cache entry ({ErrorType})", exception.GetType().Name);
        }

        await TryRemoveCacheAsync(key, cancellationToken);
        return (false, null);
    }

    private async Task TryWriteCacheAsync<T>(string key, T value, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, CacheJsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = duration }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("RPGGeek response cache write failed ({ErrorType})", exception.GetType().Name);
        }
    }

    private async Task TryRemoveCacheAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Invalid RPGGeek cache entry could not be removed ({ErrorType})", exception.GetType().Name);
        }
    }

    private static string SearchCacheKey(string query)
    {
        var normalized = string.Join(' ', query.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return $"{CachePrefix}search:{hash}";
    }

    private static string ProductCacheKey(int id) => $"{CachePrefix}product:{id}";

    private static SemaphoreSlim GetResponseCacheGate(string key)
    {
        var bucket = (StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % ResponseCacheGates.Length;
        return ResponseCacheGates[bucket];
    }

    private static string? NormalizeToken(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    private static TimeSpan ReadRequestSpacing(string? value)
    {
        return double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(Math.Min(seconds, 60))
            : TimeSpan.FromSeconds(5);
    }

    private static TimeSpan ReadDuration(string? value, TimeSpan fallback, TimeSpan maximum, TimeSpan unit)
    {
        if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return fallback;

        var duration = TimeSpan.FromSeconds(Math.Min(amount * unit.TotalSeconds, maximum.TotalSeconds));
        return duration <= TimeSpan.Zero ? fallback : duration;
    }

    private static IReadOnlyList<RpgGeekSearchResult> ParseSearch(string xml)
    {
        var document = XDocument.Parse(xml);
        return document.Descendants("item")
            .Where(item => (string?)item.Attribute("type") == "rpgitem")
            .Select(item => new RpgGeekSearchResult(
                (int?)item.Attribute("id") ?? 0,
                PrimaryName(item) ?? string.Empty,
                ParseYear(item.Element("yearpublished"))))
            .Where(result => result.Id > 0 && !string.IsNullOrWhiteSpace(result.Name))
            .ToArray();
    }

    private static RpgGeekProduct? ParseProduct(string xml, int id)
    {
        var item = XDocument.Parse(xml).Descendants("item")
            .FirstOrDefault(candidate => (string?)candidate.Attribute("type") == "rpgitem");
        if (item is null) return null;

        var title = PrimaryName(item);
        if (string.IsNullOrWhiteSpace(title)) return null;

        return new RpgGeekProduct(
            id,
            title.Trim(),
            ParseYear(item.Element("yearpublished")),
            ((string?)item.Element("description"))?.Trim(),
            ReadLinkValues(item, "rpgdesigner"),
            ReadLinkValues(item, "rpgpublisher"),
            (string?)item.Element("image"));
    }

    private static string? PrimaryName(XElement item) =>
        item.Elements("name")
            .FirstOrDefault(name => (string?)name.Attribute("type") == "primary")
            ?.Attribute("value")?.Value;

    private static int? ParseYear(XElement? element) =>
        int.TryParse((string?)element?.Attribute("value"), out var year) && year is >= 1000 and <= 9999
            ? year
            : null;

    private static IReadOnlyList<string> ReadLinkValues(XElement item, string linkType) =>
        item.Elements("link")
            .Where(link => (string?)link.Attribute("type") == linkType)
            .Select(link => (string?)link.Attribute("value"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record ProductCacheEnvelope(RpgGeekProduct? Product);
}
