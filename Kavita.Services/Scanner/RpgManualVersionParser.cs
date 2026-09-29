using System;
using System.IO;
using System.Linq;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Parser;

namespace Kavita.Services.Scanner;

internal static class RpgManualVersionParser
{
    private static readonly string[] VersionMarkers = ["Single Pages", "Pages", "Spreads"];

    public static ParserInfo? Prepare(ParserInfo? info, string path, string? gameFolder)
    {
        if (info is null) return null;

        if (!string.IsNullOrWhiteSpace(gameFolder))
        {
            info.Series = gameFolder;
            info.LocalizedSeries = string.Empty;
        }

        var fileTitle = Path.GetFileNameWithoutExtension(path);
        // The game's name can be part of the actual product title. Only remove a version suffix,
        // never the game prefix (e.g. "The Silt Verses - Rulebook" is not just "Rulebook").
        var manualTitle = fileTitle;
        var versionMarker = VersionMarkers.FirstOrDefault(marker => HasVersionSuffix(fileTitle, marker));
        if (versionMarker is not null)
        {
            manualTitle = RemoveVersionSuffix(manualTitle, versionMarker);
        }

        manualTitle = manualTitle.Trim(' ', '-', '_');
        if (string.IsNullOrWhiteSpace(manualTitle))
        {
            manualTitle = string.IsNullOrWhiteSpace(gameFolder) ? fileTitle : gameFolder.Trim(' ', '-', '_');
        }
        if (string.IsNullOrWhiteSpace(manualTitle)) return null;

        var format = info.Format switch
        {
            MangaFormat.Pdf => "PDF",
            MangaFormat.Epub => "EPUB",
            MangaFormat.Archive => "Archive",
            MangaFormat.Image => "Images",
            _ => info.Format.ToString()
        };

        info.Title = versionMarker is null ? format : $"{format} ({versionMarker})";
        info.Volumes = manualTitle;
        info.Chapters = Parser.DefaultChapter;
        info.IsSpecial = true;
        return info;
    }

    private static bool HasVersionSuffix(string title, string marker) =>
        title.EndsWith(" - " + marker, StringComparison.OrdinalIgnoreCase) ||
        title.EndsWith("_" + marker, StringComparison.OrdinalIgnoreCase);

    private static string RemoveVersionSuffix(string title, string marker)
    {
        if (title.Equals(marker, StringComparison.OrdinalIgnoreCase)) return string.Empty;

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
