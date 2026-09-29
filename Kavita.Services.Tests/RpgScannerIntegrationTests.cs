using Hangfire;
using Kavita.API.Repositories;
using Kavita.API.Services;
using Kavita.Database.Tests;
using Kavita.Models;
using Kavita.Models.Builders;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Entities.Progress;
using Kavita.Models.Metadata;
using Kavita.Models.Parser;
using Kavita.Services.Builders;
using Kavita.Services.Metadata;
using Kavita.Services.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit.Abstractions;

namespace Kavita.Services.Tests;

public class RpgScannerIntegrationTests : AbstractDbTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public RpgScannerIntegrationTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        GlobalConfiguration.Configuration.UseInMemoryStorage();
    }

    [Fact]
    public async Task ScanLibrary_Rpg_GroupsPagesAndSpreadsAsPublicationVersionsAndKeepsResourcesSeparate()
    {
        var (unitOfWork, _, _) = await CreateDatabase();
        var root = CreateRpgFiles(includeEpub: true);

        try
        {
            var library = await CreateRpgLibrary(unitOfWork, root);
            var scannerHelper = new ScannerHelper(unitOfWork, _testOutputHelper);
            var bookService = CreateBookService();
            var scanner = scannerHelper.CreateServices(bookService: bookService);

            await scanner.ScanLibrary(library.Id);

            var scannedLibrary = await unitOfWork.LibraryRepository.GetLibraryForIdAsync(library.Id, LibraryIncludes.Series);
            Assert.NotNull(scannedLibrary);
            bookService.Received(1).ParseInfo(Arg.Is<string>(path => path.EndsWith("Core Book.epub", StringComparison.OrdinalIgnoreCase)));
            var game = Assert.Single(scannedLibrary.Series);
            Assert.Equal("Pirate Borg", game.Name);
            Assert.Equal(3, game.Volumes.Count);

            var publication = Assert.Single(game.Volumes, volume => volume.LookupName == "Core Book");
            Assert.Equal(RpgMaterialType.Unclassified, publication.RpgMaterialType);
            Assert.Equal(3, publication.Chapters.Count);
            Assert.Equal(
                new[] {"EPUB", "PDF (Pages)", "PDF (Spreads)"},
                publication.Chapters.Select(chapter => chapter.Title).OrderBy(title => title).ToArray());
            Assert.Equal("en", Assert.Single(publication.Chapters, chapter => chapter.Title == "PDF (Pages)").Language);
            Assert.Equal("es", Assert.Single(publication.Chapters, chapter => chapter.Title == "PDF (Spreads)").Language);
            Assert.All(publication.Chapters, chapter => Assert.Single(chapter.Files));

            Assert.Contains(game.Volumes, volume => volume.LookupName == "PC Worksheet" && volume.Chapters.Count == 1);
            Assert.Contains(game.Volumes, volume => volume.LookupName == "Rules Cheatsheet" && volume.Chapters.Count == 1);
            Assert.All(game.Volumes, volume => Assert.Equal(RpgMaterialType.Unclassified, volume.RpgMaterialType));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, "The Silt Verses - Rulebook")]
    [InlineData(true, "Rulebook")]
    public async Task ScanLibrary_Rpg_RestoresFullPublicationTitleWithoutReplacingLegacyIdentityOrLockedNames(
        bool nameLocked, string expectedName)
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var root = Path.Combine(Path.GetTempPath(), "Kavita-Rpg-Scanner-Integration", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "The Silt Verses");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "The Silt Verses - Rulebook - Pages.pdf"), []);
        File.WriteAllBytes(Path.Combine(folder, "The Silt Verses - Rulebook - Spreads.pdf"), []);
        try
        {
            var library = await CreateRpgLibrary(unitOfWork, root);
            var scanner = new ScannerHelper(unitOfWork, _testOutputHelper).CreateServices();
            await scanner.ScanLibrary(library.Id);
            var series = await context.Series.Include(item => item.Volumes)
                .ThenInclude(volume => volume.Chapters).ThenInclude(chapter => chapter.Files)
                .SingleAsync(item => item.LibraryId == library.Id);
            var publication = Assert.Single(series.Volumes);
            Assert.Equal("The Silt Verses - Rulebook", publication.Name);
            Assert.Equal(2, publication.Chapters.Count);
            var publicationId = publication.Id;
            var originalVersionIds = publication.Chapters.Select(chapter => chapter.Id).OrderBy(id => id).ToArray();
            publication.LookupName = "Rulebook";
            publication.Name = "Rulebook";
            publication.NameLocked = nameLocked;
            publication.RpgMaterialType = RpgMaterialType.Manual;
            publication.RpgGeekId = 370894;
            publication.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;
            var admin = await context.Users.SingleAsync(user => user.UserName == "admin");
            var timestamp = DateTime.UtcNow;
            context.AppUserProgresses.Add(new AppUserProgress
            {
                AppUserId = admin.Id, ChapterId = publication.Chapters[0].Id,
                LibraryId = library.Id, SeriesId = series.Id, VolumeId = publicationId,
                PagesRead = 4, TotalReads = 1, Created = timestamp, CreatedUtc = timestamp,
                LastModified = timestamp, LastModifiedUtc = timestamp
            });
            series.LastFolderScanned = DateTime.Now.AddMinutes(-5);
            series.LastFolderScannedUtc = DateTime.UtcNow.AddMinutes(-5);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            await scanner.ScanLibrary(library.Id);
            context.ChangeTracker.Clear();
            var rescanned = await context.Series.Include(item => item.Volumes)
                .ThenInclude(volume => volume.Chapters).SingleAsync(item => item.Id == series.Id);
            var updated = Assert.Single(rescanned.Volumes);
            Assert.Equal(publicationId, updated.Id);
            Assert.Equal("The Silt Verses - Rulebook", updated.LookupName);
            Assert.Equal(expectedName, updated.Name);
            Assert.Equal(370894, updated.RpgGeekId);
            Assert.Equal(RpgMaterialType.Manual, updated.RpgMaterialType);
            Assert.Equal(originalVersionIds, updated.Chapters.Select(chapter => chapter.Id).OrderBy(id => id));
            var progress = await context.AppUserProgresses.SingleAsync();
            Assert.Equal(publicationId, progress.VolumeId);
            Assert.Equal(4, progress.PagesRead);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanLibrary_Rpg_RehomesLegacyVersionsWithoutLosingIdsExternalMetadataOrReadingProgress()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var root = CreateRpgFiles();

        try
        {
            var library = await CreateRpgLibrary(unitOfWork, root);
            var scannerHelper = new ScannerHelper(unitOfWork, _testOutputHelper);
            var scanner = scannerHelper.CreateServices();
            await scanner.ScanLibrary(library.Id);

            var series = await context.Series
                .Include(item => item.Volumes)
                .ThenInclude(volume => volume.Chapters)
                .ThenInclude(chapter => chapter.Files)
                .SingleAsync(item => item.LibraryId == library.Id);
            var publication = series.Volumes.Single(volume => volume.LookupName == "Core Book");
            var originalPublicationId = publication.Id;
            var originalVersions = publication.Chapters.ToDictionary(
                chapter => Path.GetFileName(chapter.Files.Single().FilePath),
                chapter => (chapter.Id, FileId: chapter.Files.Single().Id));

            var pagesVolume = new VolumeBuilder("Legacy Pages").WithSeriesId(series.Id).Build();
            pagesVolume.DriveThruRpgId = 399483;
            var spreadsVolume = new VolumeBuilder("Legacy Spreads").WithSeriesId(series.Id).Build();
            series.Volumes.Add(pagesVolume);
            series.Volumes.Add(spreadsVolume);
            await context.SaveChangesAsync();

            MoveVersionToVolume(publication, pagesVolume, "Core Book - Pages.pdf");
            MoveVersionToVolume(publication, spreadsVolume, "Core Book - Spreads.pdf");
            var admin = await context.Users.SingleAsync(user => user.UserName == "admin");
            var timestamp = DateTime.UtcNow;
            context.AppUserProgresses.Add(new AppUserProgress
            {
                AppUserId = admin.Id,
                ChapterId = originalVersions["Core Book - Pages.pdf"].Id,
                LibraryId = library.Id,
                SeriesId = series.Id,
                VolumeId = pagesVolume.Id,
                PagesRead = 4,
                TotalReads = 2,
                BookScrollId = "pages-version-scroll",
                Created = timestamp,
                CreatedUtc = timestamp,
                LastModified = timestamp,
                LastModifiedUtc = timestamp
            });

            series.LastFolderScanned = DateTime.Now.AddMinutes(-5);
            series.LastFolderScannedUtc = DateTime.UtcNow.AddMinutes(-5);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            await scanner.ScanLibrary(library.Id);

            var scannedLibrary = await unitOfWork.LibraryRepository.GetLibraryForIdAsync(library.Id, LibraryIncludes.Series);
            Assert.NotNull(scannedLibrary);
            var scannedSeries = Assert.Single(scannedLibrary.Series);
            Assert.Equal(3, scannedSeries.Volumes.Count);
            Assert.DoesNotContain(scannedSeries.Volumes, volume => volume.LookupName.StartsWith("Legacy ", StringComparison.Ordinal));

            var scannedPublication = Assert.Single(scannedSeries.Volumes, volume => volume.LookupName == "Core Book");
            Assert.Equal(originalPublicationId, scannedPublication.Id);
            Assert.Equal(399483, scannedPublication.DriveThruRpgId);
            Assert.Equal(2, scannedPublication.Chapters.Count);
            foreach (var (filename, identity) in originalVersions)
            {
                var version = Assert.Single(scannedPublication.Chapters,
                    chapter => Path.GetFileName(chapter.Files.Single().FilePath) == filename);
                Assert.Equal(identity.Id, version.Id);
                Assert.Equal(identity.FileId, version.Files.Single().Id);
            }

            var savedProgress = await unitOfWork.AppUserProgressRepository.GetUserProgressAsync(
                originalVersions["Core Book - Pages.pdf"].Id, admin.Id);
            Assert.NotNull(savedProgress);
            Assert.Equal(4, savedProgress.PagesRead);
            Assert.Equal(2, savedProgress.TotalReads);
            Assert.Equal("pages-version-scroll", savedProgress.BookScrollId);
            Assert.Equal(scannedPublication.Id, savedProgress.VolumeId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanLibrary_Rpg_PreservesManuallyGroupedLanguageVersionsAcrossRescans()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var root = CreateRpgFiles();
        var spanishPath = Path.Combine(root, "Pirate Borg", "Core Book Spanish.pdf");
        File.WriteAllBytes(spanishPath, []);

        try
        {
            var library = await CreateRpgLibrary(unitOfWork, root);
            var scannerHelper = new ScannerHelper(unitOfWork, _testOutputHelper);
            var scanner = scannerHelper.CreateServices();
            await scanner.ScanLibrary(library.Id);

            context.ChangeTracker.Clear();
            var series = await context.Series
                .Include(item => item.Volumes)
                .ThenInclude(volume => volume.Chapters)
                .ThenInclude(chapter => chapter.Files)
                .SingleAsync(item => item.LibraryId == library.Id);
            var primary = series.Volumes.Single(volume => volume.LookupName == "Core Book");
            var spanish = series.Volumes.Single(volume => volume.LookupName == "Core Book Spanish");
            primary.RpgMaterialType = RpgMaterialType.Manual;
            spanish.RpgMaterialType = RpgMaterialType.Manual;
            await context.SaveChangesAsync();
            var versionIds = primary.Chapters.Concat(spanish.Chapters)
                .SelectMany(chapter => chapter.Files.Select(file => (Path.GetFileName(file.FilePath), chapter.Id)))
                .ToDictionary(item => item.Item1, item => item.Id);

            var groupingService = new RpgPublicationGroupingService(unitOfWork);
            var grouped = await groupingService.GroupVersionsAsync(series.Id, primary.Id, [primary.Id, spanish.Id]);
            Assert.True(grouped.Succeeded);
            Assert.True(primary.RpgVersionGroupLocked);

            series.LastFolderScanned = DateTime.Now.AddMinutes(-5);
            series.LastFolderScannedUtc = DateTime.UtcNow.AddMinutes(-5);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await scanner.ScanLibrary(library.Id);

            context.ChangeTracker.Clear();
            var rescanned = await context.Series
                .Include(item => item.Volumes)
                .ThenInclude(volume => volume.Chapters)
                .ThenInclude(chapter => chapter.Files)
                .SingleAsync(item => item.LibraryId == library.Id);
            var publication = Assert.Single(rescanned.Volumes, volume => volume.Id == primary.Id);
            Assert.Equal(3, publication.Chapters.Count);
            Assert.DoesNotContain(rescanned.Volumes, volume => volume.LookupName == "Core Book Spanish");
            foreach (var chapter in publication.Chapters)
            {
                var filename = Path.GetFileName(Assert.Single(chapter.Files).FilePath);
                Assert.Equal(versionIds[filename], chapter.Id);
            }

            var spanishChapter = publication.Chapters.Single(chapter =>
                Path.GetFileName(Assert.Single(chapter.Files).FilePath) == "Core Book Spanish.pdf");
            var split = await groupingService.SplitVersionAsync(
                rescanned.Id, publication.Id, spanishChapter.Id, "Core Book Spanish");
            Assert.True(split.Succeeded);
            rescanned.LastFolderScanned = DateTime.Now.AddMinutes(-5);
            rescanned.LastFolderScannedUtc = DateTime.UtcNow.AddMinutes(-5);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await scanner.ScanLibrary(library.Id);

            context.ChangeTracker.Clear();
            var splitRescan = await context.Series
                .Include(item => item.Volumes)
                .ThenInclude(volume => volume.Chapters)
                .ThenInclude(chapter => chapter.Files)
                .SingleAsync(item => item.LibraryId == library.Id);
            var separatePublication = Assert.Single(splitRescan.Volumes, volume => volume.Id == split.NewVolumeId);
            Assert.Equal("Core Book Spanish", separatePublication.Name);
            Assert.Equal(spanishChapter.Id, Assert.Single(separatePublication.Chapters).Id);
            Assert.Equal(4, splitRescan.Volumes.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void MoveVersionToVolume(Volume source, Volume target, string filename)
    {
        var chapter = source.Chapters.Single(item => Path.GetFileName(item.Files.Single().FilePath) == filename);
        chapter.VolumeId = target.Id;
        chapter.Volume = target;
    }

    private static IBookService CreateBookService()
    {
        var bookService = Substitute.For<IBookService>();
        bookService.GetComicInfo(Arg.Any<string>()).Returns(callInfo =>
        {
            var filename = Path.GetFileName(callInfo.Arg<string>());
            return new ComicInfo
            {
                Series = "Embedded EPUB title",
                LanguageISO = filename.Contains("Spreads", StringComparison.OrdinalIgnoreCase) ? "es" : "en"
            };
        });
        bookService.ParseInfo(Arg.Any<string>()).Returns(callInfo =>
        {
            var path = callInfo.Arg<string>();
            return new ParserInfo
            {
                Series = "Embedded EPUB title",
                Filename = Path.GetFileName(path),
                FullFilePath = Kavita.Services.Scanner.Parser.NormalizePath(path),
                Format = MangaFormat.Epub,
                Title = Path.GetFileNameWithoutExtension(path),
                Volumes = "0",
                Chapters = "0",
                IsSpecial = true
            };
        });

        return bookService;
    }

    private static string CreateRpgFiles(bool includeEpub = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "Kavita-Rpg-Scanner-Integration", Guid.NewGuid().ToString("N"));
        var gameFolder = Path.Combine(root, "Pirate Borg");
        Directory.CreateDirectory(gameFolder);

        var filenames = new List<string>
        {
            "Core Book - Pages.pdf",
            "Core Book - Spreads.pdf",
            "PC Worksheet.pdf",
            "Rules Cheatsheet.pdf"
        };
        if (includeEpub) filenames.Add("Core Book.epub");

        foreach (var filename in filenames)
        {
            File.WriteAllBytes(Path.Combine(gameFolder, filename), []);
        }

        return root;
    }

    private static async Task<Library> CreateRpgLibrary(Kavita.API.Database.IUnitOfWork unitOfWork, string root)
    {
        var library = new LibraryBuilder("RPG Integration", LibraryType.Rpg)
            .WithFolderPath(new FolderPath {Path = root})
            .Build();
        var admin = new AppUserBuilder("admin", "admin@kavita.com", Defaults.DefaultThemes[0])
            .WithLibrary(library)
            .Build();

        unitOfWork.UserRepository.Add(admin);
        unitOfWork.LibraryRepository.Add(library);
        await unitOfWork.CommitAsync();
        return library;
    }
}
