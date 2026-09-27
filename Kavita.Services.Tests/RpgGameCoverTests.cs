using System.IO.Abstractions;
using Kavita.API.Database;
using Kavita.API.Services;
using Kavita.API.Services.Helpers;
using Kavita.API.Services.SignalR;
using Kavita.Models.Builders;
using Kavita.Models.DTOs.Settings;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kavita.Services.Tests;

public class RpgGameCoverTests
{
    [Fact]
    public void SingleUnclassifiedPdfProvidesGameCoverWithoutChangingMaterialType()
    {
        var (game, volume) = GameWithPdf("Core Book.pdf", "pdf-cover.jpg");

        Assert.Equal("pdf-cover.jpg", MetadataService.SelectRpgGameCoverImage(game));
        Assert.Equal(RpgMaterialType.Unclassified, volume.RpgMaterialType);
    }

    [Fact]
    public async Task GeneratingCoversForAnUnclassifiedGameUsesThePdfThumbnailOnTheFirstScan()
    {
        var (game, volume) = GameWithPdf("Core Book.pdf", null);
        game.Id = 42;
        game.LibraryId = 7;
        game.Library = new LibraryBuilder("RPG", LibraryType.Rpg).Build();

        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SeriesRepository.GetFullSeriesForSeriesIdAsync(game.Id, Arg.Any<CancellationToken>())
            .Returns(game);
        var cache = Substitute.For<ICacheHelper>();
        cache.ShouldUpdateCoverImage(Arg.Any<string>(), Arg.Any<MangaFile>(), Arg.Any<DateTime>(),
            Arg.Any<bool>(), Arg.Any<bool>()).Returns(true);
        cache.ShouldUpdateCoverImage(Arg.Any<string>(), null, Arg.Any<DateTime>(),
            Arg.Any<bool>(), Arg.Any<bool>()).Returns(true);
        cache.IsFileUnmodifiedSinceCreationOrLastScan(Arg.Any<Chapter>(), Arg.Any<bool>(),
            Arg.Any<MangaFile>()).Returns(true);
        var reader = Substitute.For<IReadingItemService>();
        reader.GetCoverImage(Arg.Any<string>(), Arg.Any<string>(), MangaFormat.Pdf,
            Arg.Any<EncodeFormat>(), Arg.Any<CoverImageSize>()).Returns("pdf-cover.jpg");
        var directory = Substitute.For<IDirectoryService>();
        directory.FileSystem.Returns(new FileSystem());
        directory.CoverImageDirectory.Returns("covers");
        var metadata = new MetadataService(Substitute.For<IServiceScopeFactory>(), unitOfWork,
            Substitute.For<ILogger<MetadataService>>(), Substitute.For<IEventHub>(), cache,
            reader, directory, Substitute.For<IImageService>());

        await metadata.GenerateCoversForSeries(new ServerSettingDto(), game.LibraryId, game.Id,
            forceUpdate: false, forceColorScape: false);

        Assert.Equal("pdf-cover.jpg", Assert.Single(volume.Chapters).CoverImage);
        Assert.Equal("pdf-cover.jpg", volume.CoverImage);
        Assert.Equal("pdf-cover.jpg", game.CoverImage);
        Assert.Equal(RpgMaterialType.Unclassified, volume.RpgMaterialType);
        reader.Received(1).GetCoverImage("Core Book.pdf", Arg.Any<string>(), MangaFormat.Pdf,
            Arg.Any<EncodeFormat>(), Arg.Any<CoverImageSize>());
    }

    [Fact]
    public void SinglePdfUsesItsOwnThumbnailEvenWhenAnEpubVersionExists()
    {
        var (game, volume) = GameWithPdf("Core Book.pdf", "pdf-cover.jpg");
        volume.Chapters.Add(Version("Core Book.epub", MangaFormat.Epub, "epub-cover.jpg"));

        Assert.Equal("pdf-cover.jpg", MetadataService.SelectRpgGameCoverImage(game));
    }

    [Fact]
    public void MultiplePdfsWithoutCoreManualDoNotPickAnArbitraryCover()
    {
        var (game, _) = GameWithPdf("Core Book.pdf", "first-cover.jpg");
        game.Volumes.Add(new VolumeBuilder("Supplement")
            .WithChapter(Version("Supplement.pdf", MangaFormat.Pdf, "second-cover.jpg")).Build());

        Assert.Null(MetadataService.SelectRpgGameCoverImage(game));

        game.CoverImage = "first-cover.jpg";
        Assert.Equal("first-cover.jpg", MetadataService.SelectRpgGameCoverImage(game));

        game.CoverImage = "old-cover.jpg";
        Assert.Null(MetadataService.SelectRpgGameCoverImage(game));
    }

    [Fact]
    public void OneCoreManualTakesPriorityOverSinglePdfFallback()
    {
        var (game, _) = GameWithPdf("Supplement.pdf", "pdf-cover.jpg");
        var coreManual = new VolumeBuilder("Core Book")
            .WithChapter(Version("Core Book.epub", MangaFormat.Epub, "core-cover.jpg"))
            .WithCoverImage("core-cover.jpg").Build();
        coreManual.RpgMaterialType = RpgMaterialType.CoreManual;
        game.Volumes.Add(coreManual);

        Assert.Equal("core-cover.jpg", MetadataService.SelectRpgGameCoverImage(game));
    }

    [Fact]
    public void LockedGameCoverIsNeverReplaced()
    {
        var (game, _) = GameWithPdf("Core Book.pdf", "pdf-cover.jpg");
        game.CoverImage = "chosen-cover.jpg";
        game.CoverImageLocked = true;

        Assert.Equal("chosen-cover.jpg", MetadataService.SelectRpgGameCoverImage(game));
    }

    [Fact]
    public void MissingPdfThumbnailOrNoPdfDoesNotSelectAnotherFormat()
    {
        var (game, volume) = GameWithPdf("Core Book.pdf", null);
        volume.Chapters.Add(Version("Core Book.epub", MangaFormat.Epub, "epub-cover.jpg"));

        Assert.Null(MetadataService.SelectRpgGameCoverImage(game));
        volume.Chapters.RemoveAt(0);
        Assert.Null(MetadataService.SelectRpgGameCoverImage(game));
    }

    private static (Series Game, Volume Volume) GameWithPdf(string path, string? cover)
    {
        var volume = new VolumeBuilder("Core Book")
            .WithChapter(Version(path, MangaFormat.Pdf, cover)).Build();
        return (new SeriesBuilder("Test Game").WithVolume(volume).Build(), volume);
    }

    private static Chapter Version(string path, MangaFormat format, string? cover) => new()
    {
        Range = "0",
        CoverImage = cover,
        Files = [new MangaFile {FilePath = path, Format = format}]
    };
}
