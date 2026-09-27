using System;
using System.IO;
using Kavita.Models.Parser;

namespace Kavita.Services.Scanner;

internal static class RpgManualVersionParser
{
    public static ParserInfo? Prepare(ParserInfo? info, string path, string? seriesFolder)
    {
        if (info == null) return null;

        if (!string.IsNullOrWhiteSpace(seriesFolder))
        {
            info.Series = seriesFolder;
            info.LocalizedSeries = string.Empty;
        }

        var fileTitle = Path.GetFileNameWithoutExtension(path);
        // Filenames determine manual identity. Embedded EPUB titles can describe the game rather
        // than the individual manual and must not split an EPUB from its PDF alternatives.
        var sourceTitle = fileTitle;
        var seriesPrefix = info.Series + " - ";
        if (!string.IsNullOrEmpty(info.Series) && sourceTitle.StartsWith(seriesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            sourceTitle = sourceTitle[seriesPrefix.Length..];
        }

        var versionMarker = GetVersionMarker(fileTitle);
        if (versionMarker != null)
        {
            sourceTitle = RemoveVersionSuffix(sourceTitle, versionMarker);
        }

        var manualTitle = string.IsNullOrWhiteSpace(sourceTitle) ? fileTitle : sourceTitle.Trim(' ', '-', '_');
        if (string.IsNullOrWhiteSpace(manualTitle)) return null;

        var format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        info.Title = versionMarker == null ? format : $"{format} ({versionMarker})";
        info.Volumes = manualTitle;
        info.Chapters = Parser.DefaultChapter;
        info.IsSpecial = true;
        return info;
    }

    private static string? GetVersionMarker(string fileTitle)
    {
        foreach (var marker in new[] { "Single Pages", "Pages", "Spreads" })
        {
            if (fileTitle.EndsWith(" - " + marker, StringComparison.OrdinalIgnoreCase)
                || fileTitle.EndsWith("_" + marker, StringComparison.OrdinalIgnoreCase))
            {
                return marker;
            }
        }

        return null;
    }

    private static string RemoveVersionSuffix(string title, string marker)
    {
        foreach (var separator in new[] { " - ", "_" })
        {
            var suffix = separator + marker;
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return title[..^suffix.Length];
            }
        }

        return title;
    }
}
