using System.IO;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Metadata;
using Kavita.Models.Parser;
using Kavita.Services.Scanner;
using Xunit;

namespace Kavita.Services.Tests.Parsers;

public class RpgManualVersionParserTests
{
    [Fact]
    public void Versions_share_manual_identity_but_keep_distinct_reading_titles()
    {
        var pages = Prepare("Asher's Ridge - Interactive - Pages.pdf", MangaFormat.Pdf);
        var spreads = Prepare("Asher's Ridge - Interactive - Spreads.pdf", MangaFormat.Pdf);
        var epub = Prepare("Asher's Ridge - Interactive.epub", MangaFormat.Epub);

        Assert.Equal("Asher's Ridge", pages.Series);
        Assert.Equal("Interactive", pages.Volumes);
        Assert.Equal("Interactive", spreads.Volumes);
        Assert.Equal("Interactive", epub.Volumes);
        Assert.Equal("PDF (Pages)", pages.Title);
        Assert.Equal("PDF (Spreads)", spreads.Title);
        Assert.Equal("EPUB", epub.Title);
        Assert.All(new[] { pages, spreads, epub }, info =>
        {
            Assert.Equal(Parser.DefaultChapter, info.Chapters);
            Assert.True(info.IsSpecial);
        });
    }

    [Fact]
    public void Prepare_preserves_Spanish_language_metadata_for_Heart_manual()
    {
        var info = Prepare("Heart - Core Rulebook - Pages.pdf", MangaFormat.Pdf, "Heart",
            new ComicInfo {LanguageISO = "es"});

        Assert.Equal("Heart", info.Series);
        Assert.Equal("Core Rulebook", info.Volumes);
        Assert.Equal("es", info.ComicInfo?.LanguageISO);
    }

    [Fact]
    public void Prepare_does_not_strip_specific_product_name_segments()
    {
        var info = Prepare("Asher's Ridge - Doors to Elsewhere.pdf", MangaFormat.Pdf);

        Assert.Equal("Doors to Elsewhere", info.Volumes);
        Assert.Equal("PDF", info.Title);
    }

    private static ParserInfo Prepare(string filename, MangaFormat format, string gameFolder = "Asher's Ridge",
        ComicInfo? comicInfo = null)
    {
        var path = Path.Combine("C:/RPG", gameFolder, filename);
        var info = new ParserInfo
        {
            Series = "metadata-series",
            Filename = filename,
            FullFilePath = path,
            Format = format,
            ComicInfo = comicInfo,
            Title = format == MangaFormat.Epub ? "Unrelated embedded title" : Path.GetFileNameWithoutExtension(filename),
            Volumes = "0",
            Chapters = "0"
        };

        return RpgManualVersionParser.Prepare(info, path, gameFolder)!;
    }
}
