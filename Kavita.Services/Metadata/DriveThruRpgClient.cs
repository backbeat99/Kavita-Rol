using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kavita.API.Services.Metadata;

namespace Kavita.Services.Metadata;

public interface IDriveThruRpgClient
{
    Task<IReadOnlyList<DriveThruRpgSearchResult>> SearchProductsAsync(string query, CancellationToken ct = default);
    Task<DriveThruRpgProduct?> GetProductAsync(int productId, CancellationToken ct = default);
}

/// <summary>Read-only client for DriveThruRPG's public product API. It requires no API token.</summary>
public sealed class DriveThruRpgClient(HttpClient httpClient) : IDriveThruRpgClient
{
    private const string ApiRoot = "https://api.dmsguild.com/api/vBeta";

    public async Task<IReadOnlyList<DriveThruRpgSearchResult>> SearchProductsAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var url = $"{ApiRoot}/search_ahead?groupId=29&keyword={Uri.EscapeDataString(query.Trim())}&siteId=29";
        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = document.RootElement;
        var data = root.TryGetProperty("data", out var dataElement) ? dataElement : root;
        var results = new List<DriveThruRpgSearchResult>();

        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray()) AddSearchResult(item, null, results);
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in data.EnumerateObject()) AddSearchResult(item.Value, item.Name, results);
        }

        return results
            .Where(result => result.ProductId > 0 && !string.IsNullOrWhiteSpace(result.Title))
            .DistinctBy(result => result.ProductId)
            .ToList();
    }

    public async Task<DriveThruRpgProduct?> GetProductAsync(int productId, CancellationToken ct = default)
    {
        if (productId <= 0) return null;

        using var response = await httpClient.GetAsync($"{ApiRoot}/products/{productId}", ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = document.RootElement;
        var data = root.TryGetProperty("data", out var dataElement) ? dataElement : root;
        var attributes = GetObject(data, "attributes");
        if (attributes.ValueKind == JsonValueKind.Undefined) return null;

        var description = GetObject(attributes, "description");
        var title = GetString(description, "name") ?? GetString(attributes, "name") ?? GetString(attributes, "title");
        if (string.IsNullOrWhiteSpace(title)) return null;

        var available = GetString(attributes, "dateAvailable");
        DateTime? releaseDate = DateTimeOffset.TryParse(available, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsedDate)
            ? DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc)
            : null;

        return ExternalMetadataText.Sanitize(new DriveThruRpgProduct(
            productId,
            title.Trim(),
            ReadAuthors(attributes),
            GetString(description, "description") ?? string.Empty,
            ReadPublisher(root),
            releaseDate,
            BuildCoverUrl(GetString(attributes, "image")),
            ReadLanguageCode(root, attributes)));
    }

    private static void AddSearchResult(JsonElement item, string? fallbackId, ICollection<DriveThruRpgSearchResult> results)
    {
        var attributes = GetObject(item, "attributes");
        if (attributes.ValueKind == JsonValueKind.Undefined) attributes = item;

        var entityType = GetString(attributes, "entityType");
        if (!string.IsNullOrEmpty(entityType) && !string.Equals(entityType, "Product", StringComparison.OrdinalIgnoreCase)) return;

        var id = GetInt(item, "entityId") ?? GetInt(attributes, "entityId") ?? GetInt(item, "id");
        if (!id.HasValue && int.TryParse(fallbackId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)) id = parsedId;

        var title = GetString(attributes, "name") ?? GetString(attributes, "title");
        if (id.HasValue && !string.IsNullOrWhiteSpace(title)) results.Add(ExternalMetadataText.Sanitize(new DriveThruRpgSearchResult(id.Value, title.Trim())));
    }

    private static IReadOnlyList<string> ReadAuthors(JsonElement attributes)
    {
        if (!attributes.TryGetProperty("authors", out var authors) || authors.ValueKind != JsonValueKind.Array) return [];

        return authors.EnumerateArray()
            .Select(author => author.ValueKind == JsonValueKind.String ? author.GetString() : GetString(author, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ReadPublisher(JsonElement root)
    {
        if (!root.TryGetProperty("included", out var included) || included.ValueKind != JsonValueKind.Array) return null;

        foreach (var entity in included.EnumerateArray())
        {
            if (!string.Equals(GetString(entity, "type"), "Publisher", StringComparison.OrdinalIgnoreCase)) continue;
            var publisher = GetString(GetObject(entity, "attributes"), "name");
            if (!string.IsNullOrWhiteSpace(publisher)) return publisher.Trim();
        }

        return null;
    }

    private static string? ReadLanguageCode(JsonElement root, JsonElement attributes)
    {
        if (TryNormalizeLanguageCode(GetString(attributes, "languageCode"), out var code)) return code;
        if (TryNormalizeLanguageName(GetString(attributes, "language"), out code) ||
            TryNormalizeLanguageCode(GetString(attributes, "language"), out code)) return code;

        if (root.TryGetProperty("included", out var included) && included.ValueKind == JsonValueKind.Array)
        {
            foreach (var entity in included.EnumerateArray())
            {
                var entityType = GetString(entity, "type");
                if (entityType == null || !entityType.Contains("filter", StringComparison.OrdinalIgnoreCase)) continue;
                var entityAttributes = GetObject(entity, "attributes");
                if (GetInt(entityAttributes, "parentId") != 40 && GetInt(entityAttributes, "parent_id") != 40) continue;
                if (TryReadLanguageFilter(entityAttributes, out code)) return code;
            }
        }

        if (attributes.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Array)
        {
            foreach (var filter in filters.EnumerateArray())
            {
                var filterAttributes = GetObject(filter, "attributes");
                if (GetInt(filterAttributes, "parentId") != 40 && GetInt(filterAttributes, "parent_id") != 40) continue;
                if (TryReadLanguageFilter(filterAttributes.ValueKind == JsonValueKind.Undefined ? filter : filterAttributes, out code)) return code;
            }
        }

        return null;
    }

    private static bool TryReadLanguageFilter(JsonElement filter, out string? code)
    {
        var name = GetString(filter, "name") ?? GetString(GetObject(filter, "description"), "name");
        if (TryNormalizeLanguageName(name, out code) || TryNormalizeLanguageCode(name, out code)) return true;

        if (filter.TryGetProperty("descriptions", out var descriptions) && descriptions.ValueKind == JsonValueKind.Array)
        {
            foreach (var description in descriptions.EnumerateArray())
            {
                name = GetString(description, "name");
                if (TryNormalizeLanguageName(name, out code) || TryNormalizeLanguageCode(name, out code)) return true;
            }
        }

        code = null;
        return false;
    }

    private static bool TryNormalizeLanguageName(string? language, out string? code)
    {
        code = null;
        if (string.IsNullOrWhiteSpace(language)) return false;

        code = language.Trim().ToLowerInvariant() switch
        {
            "english" => "en",
            "spanish" or "español" => "es",
            "french" or "français" => "fr",
            "german" or "deutsch" => "de",
            "italian" or "italiano" => "it",
            "portuguese" or "português" => "pt",
            "japanese" or "日本語" => "ja",
            "chinese" or "中文" => "zh",
            _ => null
        };
        return code != null;
    }

    private static bool TryNormalizeLanguageCode(string? value, out string? code)
    {
        code = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Trim().Split('-');
        if (parts[0].Length is < 2 or > 3 || !parts[0].All(char.IsAsciiLetter)) return false;
        if (parts.Skip(1).Any(part => part.Length is < 2 or > 8 || !part.All(char.IsAsciiLetterOrDigit))) return false;
        code = value.Trim().ToLowerInvariant();
        return true;
    }

    private static string? BuildCoverUrl(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        if (Uri.TryCreate(image, UriKind.Absolute, out var absolute))
            return absolute.Scheme is "http" or "https" ? absolute.ToString() : null;

        var path = string.Join('/', image.TrimStart('/').Split('/').Select(Uri.EscapeDataString));
        return $"https://www.drivethrurpg.com/images/{path}";
    }

    private static JsonElement GetObject(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        return value.ValueKind == JsonValueKind.Number ? value.ToString() : null;
    }

    private static int? GetInt(JsonElement element, string propertyName) =>
        int.TryParse(GetString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : null;
}
