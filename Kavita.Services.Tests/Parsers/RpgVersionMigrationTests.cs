using System.Collections.Generic;
using System.Linq;
using Kavita.Models.Builders;
using Kavita.Models.Entities;
using Kavita.Models.Parser;
using Kavita.Services.Builders;
using Kavita.Services.Scanner;
using Xunit;

namespace Kavita.Services.Tests.Parsers;

public class RpgVersionMigrationTests
{
    [Fact]
    public void RehomeRpgVersions_RetainsChapterIdentityAndLegacyProductIdBeforeOldVolumeIsRemoved()
    {
        var pages = new ChapterBuilder(Parser.DefaultChapter, "Interactive - Pages.pdf").WithId(100).Build();
        pages.Files.Add(new MangaFile { FilePath = "C:/RPG/Asher's Ridge/Interactive - Pages.pdf" });
        pages.DriveThruRpgId = 42;
        var spreads = new ChapterBuilder(Parser.DefaultChapter, "Interactive - Spreads.pdf").WithId(101).Build();
        spreads.Files.Add(new MangaFile { FilePath = "C:/RPG/Asher's Ridge/Interactive - Spreads.pdf" });
        var oldVolume = new VolumeBuilder(Parser.LooseLeafVolume).WithChapter(pages).WithChapter(spreads).Build();
        var series = new SeriesBuilder("Asher's Ridge").WithVolume(oldVolume).Build();
        var parsed = new List<ParserInfo>
        {
            new() { Series = series.Name, Volumes = "Interactive", FullFilePath = pages.Files.Single().FilePath },
            new() { Series = series.Name, Volumes = "Interactive", FullFilePath = spreads.Files.Single().FilePath }
        };

        ProcessSeries.RehomeRpgVersions(series, parsed);

        var manual = series.Volumes.Single(volume => volume.LookupName == "Interactive");
        Assert.Empty(oldVolume.Chapters);
        Assert.Equal(42, manual.DriveThruRpgId);
        Assert.Contains(pages, manual.Chapters);
        Assert.Contains(spreads, manual.Chapters);
        Assert.Equal(100, pages.Id);
        Assert.Equal(101, spreads.Id);
        Assert.Same(manual, pages.Volume);
    }

    [Fact]
    public void RehomeRpgVersions_DoesNotChooseAmongConflictingLegacyIds()
    {
        var pages = new ChapterBuilder(Parser.DefaultChapter, "Pages.pdf").Build();
        pages.Files.Add(new MangaFile { FilePath = "C:/RPG/Pages.pdf" });
        pages.DriveThruRpgId = 42;
        var spreads = new ChapterBuilder(Parser.DefaultChapter, "Spreads.pdf").Build();
        spreads.Files.Add(new MangaFile { FilePath = "C:/RPG/Spreads.pdf" });
        spreads.DriveThruRpgId = 43;
        var series = new SeriesBuilder("RPG")
            .WithVolume(new VolumeBuilder(Parser.LooseLeafVolume).WithChapter(pages).WithChapter(spreads).Build())
            .Build();
        var parsed = new List<ParserInfo>
        {
            new() { Series = series.Name, Volumes = "Manual", FullFilePath = pages.Files.Single().FilePath },
            new() { Series = series.Name, Volumes = "Manual", FullFilePath = spreads.Files.Single().FilePath }
        };

        ProcessSeries.RehomeRpgVersions(series, parsed);

        var manual = series.Volumes.Single(volume => volume.LookupName == "Manual");
        Assert.Null(manual.DriveThruRpgId);
        Assert.Equal(new int?[] { 42, 43 }, manual.Chapters.Select(chapter => chapter.DriveThruRpgId).ToArray());
    }
}
