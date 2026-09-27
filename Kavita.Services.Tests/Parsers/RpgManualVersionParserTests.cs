using System.IO;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Parser;
using Kavita.Services.Scanner;
using Xunit;

namespace Kavita.Services.Tests.Parsers;

public class RpgManualVersionParserTests
{
    [Fact]
    public void Prepare_GroupsPagesSpreadsAndEpubIntoOneManualWithIndependentVersionLabels()
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
        Assert.All(new[] {pages, spreads, epub}, info =>
        {
            Assert.Equal(Parser.DefaultChapter, info.Chapters);
            Assert.True(info.IsSpecial);
        });
    }

    [Fact]
    public void Prepare_DoesNotStripSpecificProductNameSegments()
    {
        var info = Prepare("Asher's Ridge - Doors to Elsewhere.pdf", MangaFormat.Pdf);

        Assert.Equal("Doors to Elsewhere", info.Volumes);
        Assert.Equal("PDF", info.Title);
    }

    private static ParserInfo Prepare(string filename, MangaFormat format)
    {
        var path = Path.Combine("C:/RPG/Asher's Ridge", filename);
        var info = new ParserInfo
        {
            Series = "metadata-series",
            Filename = filename,
            FullFilePath = path,
            Format = format,
            Title = format == MangaFormat.Epub ? "Unrelated embedded title" : Path.GetFileNameWithoutExtension(filename),
            Volumes = "0",
            Chapters = "0"
        };

        return RpgManualVersionParser.Prepare(info, path, "Asher's Ridge")!;
    }
}
