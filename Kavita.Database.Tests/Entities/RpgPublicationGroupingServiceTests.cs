using System;
using System.Linq;
using System.Threading.Tasks;
using Kavita.API.Services.Metadata;
using Kavita.Models.Builders;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Entities.Progress;
using Kavita.Models.Entities.User;
using Kavita.Services.Builders;
using Kavita.Services.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Kavita.Database.Tests.Entities;

public class RpgPublicationGroupingServiceTests(ITestOutputHelper output) : AbstractDbTest(output)
{
    [Fact]
    public async Task GroupVersions_MovesChaptersAndProgress_PreservesLanguageAndProviderIdentity()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var englishChapter = new ChapterBuilder("0", "Player's Guide.pdf").WithPages(120).Build();
        englishChapter.Language = "en";
        var spanishChapter = new ChapterBuilder("0", "Guía del jugador.pdf").WithPages(132).Build();
        spanishChapter.Language = "es";
        var english = new VolumeBuilder("Player's Guide")
            .WithChapter(englishChapter)
            .Build();
        english.RpgMaterialType = RpgMaterialType.Manual;
        english.RpgGeekId = 290374;
        english.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;
        english.Summary = "Shared summary";
        var spanish = new VolumeBuilder("Guía del jugador")
            .WithChapter(spanishChapter)
            .Build();
        spanish.RpgMaterialType = RpgMaterialType.Manual;
        spanish.RpgWriters = ["Designer"];
        var series = new SeriesBuilder("Heart").WithVolumes([english, spanish]).Build();
        var library = new LibraryBuilder("RPG", LibraryType.Rpg).WithSeries(series).Build();
        var user = new AppUserBuilder("reader", "reader@example.com").WithLibrary(library).Build();
        context.Users.Add(user);
        context.Library.Add(library);
        await unitOfWork.CommitAsync();

        var timestamp = DateTime.UtcNow;
        context.AppUserProgresses.Add(new AppUserProgress
        {
            AppUserId = user.Id,
            ChapterId = spanishChapter.Id,
            VolumeId = spanish.Id,
            SeriesId = series.Id,
            LibraryId = library.Id,
            PagesRead = 37,
            TotalReads = 2,
            BookScrollId = "spanish-progress",
            Created = timestamp,
            CreatedUtc = timestamp,
            LastModified = timestamp,
            LastModifiedUtc = timestamp
        });
        await unitOfWork.CommitAsync();

        var service = new RpgPublicationGroupingService(unitOfWork);
        var result = await service.GroupVersionsAsync(series.Id, english.Id, [english.Id, spanish.Id]);

        Assert.True(result.Succeeded);
        Assert.Equal(english.Id, result.PrimaryVolumeId);
        Assert.Equal([spanish.Id], result.RemovedVolumeIds);
        Assert.True(english.RpgVersionGroupLocked);
        Assert.Equal("Player's Guide", english.Name);
        Assert.Equal(290374, english.RpgGeekId);
        Assert.Equal(["Designer"], english.RpgWriters);
        Assert.Equal(2, english.Chapters.Count);

        context.ChangeTracker.Clear();
        var grouped = await context.Volume
            .Include(volume => volume.Chapters)
            .SingleAsync(volume => volume.Id == english.Id);
        Assert.Equal(new[] {"en", "es"}, grouped.Chapters.Select(chapter => chapter.Language).OrderBy(language => language).ToArray());
        var savedProgress = await context.AppUserProgresses.SingleAsync(progress => progress.ChapterId == spanishChapter.Id);
        Assert.Equal(english.Id, savedProgress.VolumeId);
        Assert.Equal(37, savedProgress.PagesRead);
        Assert.Equal(2, savedProgress.TotalReads);
        Assert.Equal("spanish-progress", savedProgress.BookScrollId);
        Assert.False(await context.Volume.AnyAsync(volume => volume.Id == spanish.Id));
    }

    [Fact]
    public async Task SplitVersion_RestoresIndependentPublicationAndPreservesVersionProgress()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var englishChapter = new ChapterBuilder("0", "Player's Guide.pdf").WithPages(120).Build();
        englishChapter.Language = "en";
        var spanishChapter = new ChapterBuilder("0", "Guía del jugador.pdf").WithPages(132).Build();
        spanishChapter.Language = "es";
        var english = new VolumeBuilder("Player's Guide").WithChapter(englishChapter).Build();
        english.RpgMaterialType = RpgMaterialType.Manual;
        english.RpgGeekId = 290374;
        english.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;
        english.Summary = "Shared bibliography";
        var spanish = new VolumeBuilder("Guía del jugador").WithChapter(spanishChapter).Build();
        spanish.RpgMaterialType = RpgMaterialType.Manual;
        var series = new SeriesBuilder("Heart").WithVolumes([english, spanish]).Build();
        var library = new LibraryBuilder("RPG", LibraryType.Rpg).WithSeries(series).Build();
        var user = new AppUserBuilder("reader", "reader@example.com").WithLibrary(library).Build();
        context.Users.Add(user);
        context.Library.Add(library);
        await unitOfWork.CommitAsync();

        var timestamp = DateTime.UtcNow;
        context.AppUserProgresses.Add(new AppUserProgress
        {
            AppUserId = user.Id,
            ChapterId = spanishChapter.Id,
            VolumeId = spanish.Id,
            SeriesId = series.Id,
            LibraryId = library.Id,
            PagesRead = 41,
            TotalReads = 3,
            BookScrollId = "split-progress",
            Created = timestamp,
            CreatedUtc = timestamp,
            LastModified = timestamp,
            LastModifiedUtc = timestamp
        });
        await unitOfWork.CommitAsync();

        var service = new RpgPublicationGroupingService(unitOfWork);
        var grouped = await service.GroupVersionsAsync(series.Id, english.Id, [english.Id, spanish.Id]);
        Assert.True(grouped.Succeeded);

        var split = await service.SplitVersionAsync(series.Id, english.Id, spanishChapter.Id, "Player's Guide (Spanish)");

        Assert.True(split.Succeeded);
        Assert.NotEqual(english.Id, split.NewVolumeId);
        context.ChangeTracker.Clear();
        var publications = await context.Volume
            .Include(volume => volume.Chapters)
            .Where(volume => volume.SeriesId == series.Id)
            .OrderBy(volume => volume.Id)
            .ToListAsync();
        Assert.Equal(2, publications.Count);
        Assert.Single(publications.Single(volume => volume.Id == english.Id).Chapters);
        var spanishPublication = publications.Single(volume => volume.Id == split.NewVolumeId);
        var preservedChapter = Assert.Single(spanishPublication.Chapters);
        Assert.Equal(spanishChapter.Id, preservedChapter.Id);
        Assert.Equal("es", preservedChapter.Language);
        Assert.Equal("Player's Guide (Spanish)", spanishPublication.Name);
        Assert.Equal(RpgMaterialType.Manual, spanishPublication.RpgMaterialType);
        Assert.Equal("Shared bibliography", spanishPublication.Summary);
        Assert.Null(spanishPublication.RpgGeekId);
        Assert.True(spanishPublication.RpgVersionGroupLocked);

        var savedProgress = await context.AppUserProgresses.SingleAsync(progress => progress.ChapterId == spanishChapter.Id);
        Assert.Equal(split.NewVolumeId, savedProgress.VolumeId);
        Assert.Equal(41, savedProgress.PagesRead);
        Assert.Equal(3, savedProgress.TotalReads);
        Assert.Equal("split-progress", savedProgress.BookScrollId);
    }

    [Fact]
    public async Task GroupVersions_AllowsUnclassifiedItemsButRejectsResources()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var first = new VolumeBuilder("Core Book").WithChapter(new ChapterBuilder("0").Build()).Build();
        var second = new VolumeBuilder("Core Book Spanish").WithChapter(new ChapterBuilder("0").Build()).Build();
        var resource = new VolumeBuilder("Map").WithChapter(new ChapterBuilder("0").Build()).Build();
        resource.RpgMaterialType = RpgMaterialType.Map;
        var series = new SeriesBuilder("Game").WithVolumes([first, second, resource]).Build();
        context.Library.Add(new LibraryBuilder("RPG", LibraryType.Rpg).WithSeries(series).Build());
        await unitOfWork.CommitAsync();
        var service = new RpgPublicationGroupingService(unitOfWork);

        var grouped = await service.GroupVersionsAsync(series.Id, first.Id, [first.Id, second.Id]);
        var rejected = await service.GroupVersionsAsync(series.Id, first.Id, [first.Id, resource.Id]);

        Assert.True(grouped.Succeeded);
        Assert.Equal(RpgMaterialType.Unclassified, first.RpgMaterialType);
        Assert.False(rejected.Succeeded);
        Assert.Equal(RpgPublicationGroupingError.ResourceSelected, rejected.Error);
    }

    [Fact]
    public async Task GroupVersions_RequiresLinkedPublicationToRemainPrimary()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var first = new VolumeBuilder("English").WithChapter(new ChapterBuilder("0").Build()).Build();
        first.RpgMaterialType = RpgMaterialType.Manual;
        var linked = new VolumeBuilder("Español").WithChapter(new ChapterBuilder("0").Build()).Build();
        linked.RpgMaterialType = RpgMaterialType.Manual;
        linked.RpgGeekId = 290374;
        var series = new SeriesBuilder("Game").WithVolumes([first, linked]).Build();
        context.Library.Add(new LibraryBuilder("RPG", LibraryType.Rpg).WithSeries(series).Build());
        await unitOfWork.CommitAsync();
        var service = new RpgPublicationGroupingService(unitOfWork);

        var result = await service.GroupVersionsAsync(series.Id, first.Id, [first.Id, linked.Id]);

        Assert.False(result.Succeeded);
        Assert.Equal(RpgPublicationGroupingError.ProviderIdentityNotPrimary, result.Error);
        Assert.Equal(2, await context.Volume.CountAsync(volume => volume.SeriesId == series.Id));
        Assert.Equal(290374, linked.RpgGeekId);
    }

    [Fact]
    public async Task GroupVersions_RejectsConflictingProviderIdentitiesWithoutDeletingEitherItem()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var first = new VolumeBuilder("English").WithChapter(new ChapterBuilder("0").Build()).Build();
        first.RpgMaterialType = RpgMaterialType.Manual;
        first.RpgGeekId = 100;
        var second = new VolumeBuilder("Español").WithChapter(new ChapterBuilder("0").Build()).Build();
        second.RpgMaterialType = RpgMaterialType.Manual;
        second.RpgGeekId = 200;
        var series = new SeriesBuilder("Game").WithVolumes([first, second]).Build();
        context.Library.Add(new LibraryBuilder("RPG", LibraryType.Rpg).WithSeries(series).Build());
        await unitOfWork.CommitAsync();
        var service = new RpgPublicationGroupingService(unitOfWork);

        var result = await service.GroupVersionsAsync(series.Id, first.Id, [first.Id, second.Id]);

        Assert.False(result.Succeeded);
        Assert.Equal(RpgPublicationGroupingError.ConflictingProviderIds, result.Error);
        Assert.Equal(2, await context.Volume.CountAsync(volume => volume.SeriesId == series.Id));
        Assert.Equal(100, first.RpgGeekId);
        Assert.Equal(200, second.RpgGeekId);
    }
}
