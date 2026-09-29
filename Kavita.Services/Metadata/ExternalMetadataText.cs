using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Kavita.API.Services.Metadata;

namespace Kavita.Services.Metadata;

/// <summary>
/// Converts the HTML and entities served by RPG providers into the plain text Kavita stores and shows.
/// Names and titles collapse to a single line; descriptions keep paragraph breaks as newlines.
/// </summary>
internal static partial class ExternalMetadataText
{
    [GeneratedRegex(@"<\s*/?\s*(?:br|p|div|li|tr|h[1-6])\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    // Only real tag shapes are removed, so a comparison like "a < b" survives entity decoding.
    [GeneratedRegex(@"</?[a-zA-Z][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex HorizontalSpaceRegex();

    [GeneratedRegex(@" *\n *")]
    private static partial Regex NewlineSpacingRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraNewlineRegex();

    public static string CleanText(string? value) =>
        CleanDescription(value).Replace('\n', ' ').Trim();

    public static string CleanDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        // Some feeds double-encode entities (&amp;rsquo;) or even tags (&lt;br /&gt;).
        var text = value;
        for (var pass = 0; pass < 3; pass++)
        {
            var decoded = WebUtility.HtmlDecode(text);
            if (string.Equals(decoded, text, StringComparison.Ordinal)) break;
            text = decoded;
        }

        text = BlockTagRegex().Replace(text, "\n");
        text = TagRegex().Replace(text, string.Empty);
        text = text.Replace('\u00A0', ' ');
        text = HorizontalSpaceRegex().Replace(text, " ");
        text = NewlineSpacingRegex().Replace(text, "\n");
        text = ExtraNewlineRegex().Replace(text, "\n\n");
        return text.Trim();
    }

    public static RpgGeekProduct Sanitize(RpgGeekProduct product) => product with
    {
        Title = CleanText(product.Title),
        Description = EmptyToNull(CleanDescription(product.Description)),
        Designers = CleanNames(product.Designers),
        Publishers = CleanNames(product.Publishers)
    };

    public static RpgGeekSearchResult Sanitize(RpgGeekSearchResult result) =>
        result with { Name = CleanText(result.Name) };

    public static DriveThruRpgProduct Sanitize(DriveThruRpgProduct product) => product with
    {
        Title = CleanText(product.Title),
        Description = CleanDescription(product.Description),
        Authors = CleanNames(product.Authors),
        Publisher = EmptyToNull(CleanText(product.Publisher))
    };

    public static DriveThruRpgSearchResult Sanitize(DriveThruRpgSearchResult result) =>
        result with { Title = CleanText(result.Title) };

    private static IReadOnlyList<string> CleanNames(IReadOnlyList<string> names) => names
        .Select(CleanText)
        .Where(name => name.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
}
