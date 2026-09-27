using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Kavita.Services.Metadata;

public interface IRpgGeekClient
{
    bool IsEnabled { get; }
    Task<IReadOnlyList<RpgGeekSearchResult>> SearchProductsAsync(string query, CancellationToken ct = default);
    Task<RpgGeekProduct?> GetProductAsync(int id, CancellationToken ct = default);
}

/// <summary>
/// Client for the RPGGeek (Geekdo XML API 2). Results are cached in memory to avoid
/// repeating searches, and requests are spaced to respect the API's usage policy.
/// Requires the BGG_API_TOKEN configuration value; without it the client stays inactive
/// and never performs HTTP requests.
/// </summary>
public sealed class RpgGeekClient(HttpClient httpClient, IConfiguration configuration, IMemoryCache cache) : IRpgGeekClient
{
    private const string ApiRoot = "https://api.geekdo.com/xmlapi2";
    private const string UserAgent = "Kavita-Rol/0.9.1 RPGGeek metadata client";
    private static readonly SemaphoreSlim RateGate = new(1, 1);
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(1250);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    private readonly string? _apiToken = NormalizeToken(configuration["BGG_API_TOKEN"]);

    public bool IsEnabled => _apiToken != null;

    public async Task<IReadOnlyList<RpgGeekSearchResult>> SearchProductsAsync(string query, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(query)) return [];

        var cacheKey = "rpggeek-search:" + query.Trim().ToLowerInvariant();
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.Size = 1;
            var xml = await GetXmlAsync($"{ApiRoot}/search?query={Uri.EscapeDataString(query.Trim())}&type=rpgitem", ct);
            return ParseSearch(xml);
        }) ?? [];
    }

    public async Task<RpgGeekProduct?> GetProductAsync(int id, CancellationToken ct = default)
    {
        if (!IsEnabled || id <= 0) return null;

        var cacheKey = "rpggeek-product:" + id;
        return await cache.GetOrCreateAsync<RpgGeekProduct?>(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.Size = 1;
            var xml = await GetXmlAsync($"{ApiRoot}/thing?id={id}&type=rpgitem", ct);
            return ParseProduct(xml, id);
        });
    }

    private async Task<string> GetXmlAsync(string url, CancellationToken ct)
    {
        await RateGate.WaitAsync(ct);
        try
        {
            await Task.Delay(RequestSpacing, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiToken}");
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
        finally
        {
            RateGate.Release();
        }
    }

    private static string? NormalizeToken(string? token)
    {
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
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
            .ToList();
    }

    private static RpgGeekProduct? ParseProduct(string xml, int id)
    {
        var document = XDocument.Parse(xml);
        var item = document.Descendants("item")
            .FirstOrDefault(candidate => (string?)candidate.Attribute("type") == "rpgitem");
        if (item == null) return null;

        var title = PrimaryName(item);
        if (string.IsNullOrWhiteSpace(title)) return null;

        var designers = ReadLinkValues(item, "rpgdesigner");
        var publishers = ReadLinkValues(item, "rpgpublisher");

        return new RpgGeekProduct(
            id,
            title.Trim(),
            ParseYear(item.Element("yearpublished")),
            ((string?)item.Element("description") ?? string.Empty).Trim(),
            designers,
            publishers,
            (string?)item.Element("image"));
    }

    private static string? PrimaryName(XElement item)
    {
        return item.Elements("name")
            .FirstOrDefault(name => (string?)name.Attribute("type") == "primary")
            ?.Attribute("value") is { } value
            ? value.Value
            : null;
    }

    private static int? ParseYear(XElement? element)
    {
        return int.TryParse((string?)element?.Attribute("value"), out var year) ? year : null;
    }

    private static IReadOnlyList<string> ReadLinkValues(XElement item, string linkType)
    {
        return item.Elements("link")
            .Where(link => (string?)link.Attribute("type") == linkType)
            .Select(link => (string?)link.Attribute("value"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
