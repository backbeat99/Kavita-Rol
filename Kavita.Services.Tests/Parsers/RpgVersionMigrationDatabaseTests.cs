using Kavita.Database.Tests;
using Kavita.Models.Builders;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Parser;
using Kavita.Services.Builders;
using Kavita.Services.Scanner;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Kavita.Services.Tests.Parsers;

public class RpgVersionMigrationDatabaseTests(ITestOutputHelper output) : AbstractDbTest(output)
{
    [Fact]
    public async Task RehomeRpgVersions_PersistsOldChapterIdsAfterDeletingFormerVolume()
    {
        var (_, context, _) = await CreateDatabase();
        var library = new LibraryBuilder("RPG", LibraryType.Rpg).Build();
        context.Library.Add(library);
        await context.SaveChangesAsync();

        var version = new ChapterBuilder(Parser.DefaultChapter, "Interactive - Pages.pdf").Build();
        version.Files.Add(new MangaFile { FilePath = "C:/RPG/Interactive - Pages.pdf" });
        var oldVolume = new VolumeBuilder(Parser.LooseLeafVolume).WithChapter(version).Build();
        var series = new SeriesBuilder("Game").WithLibraryId(library.Id).WithVolume(oldVolume).Build();
        context.Series.Add(series);
        await context.SaveChangesAsync();
        var chapterId = version.Id;
        var oldVolumeId = oldVolume.Id;

        ProcessSeries.RehomeRpgVersions(series, [new ParserInfo
        {
            Series = series.Name, Volumes = "Interactive", FullFilePath = version.Files.Single().FilePath
        }]);
        // Scanner must fix up EF relationships before RemoveVolumes marks the old parent as deleted.
        context.ChangeTracker.DetectChanges();
        var target = series.Volumes.Single(volume => volume.LookupName == "Interactive");
        Assert.Same(target, version.Volume);
        Assert.Equal(EntityState.Added, context.Entry(target).State);
        context.Volume.Remove(oldVolume);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var savedVersion = await context.Chapter.Include(chapter => chapter.Volume).SingleAsync(chapter => chapter.Id == chapterId);
        Assert.Equal("Interactive", savedVersion.Volume.LookupName);
        Assert.False(await context.Volume.AnyAsync(volume => volume.Id == oldVolumeId));
    }
}
