using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Kavita.API.Services.Metadata;
using Kavita.API.Services.SignalR;
using Kavita.Database;
using Kavita.Models.AutoMapper;
using Kavita.Models.Builders;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Builders;
using Kavita.Services.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Kavita.Services.Tests.Metadata;

public class DriveThruRpgMetadataServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private DataContext _context = null!;
    private IDriveThruRpgClient _client = null!;
    private ICoverDbService _coverDbService = null!;
    private IEventHub _eventHub = null!;
    private DriveThruRpgMetadataService _service = null!;
    private int _libraryId;
    private int _seriesId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DataContext>().UseSqlite(_connection).Options;
        _context = new DataContext(options);
        await _context.Database.EnsureCreatedAsync();

        var library = new LibraryBuilder("RPG").Build();
        library.Type = LibraryType.Rpg;
        library.EnableDriveThruRpgMetadata = true;
        _context.Library.Add(library);
        await _context.SaveChangesAsync();
        _libraryId = library.Id;

        var series = new SeriesBuilder("Test RPG").WithLibraryId(_libraryId).Build();
        _context.Series.Add(series);
        await _context.SaveChangesAsync();
        _seriesId = series.Id;

        var mapperConfig = new MapperConfiguration(config => config.AddMaps(typeof(AutoMapperProfiles).Assembly));
        var unitOfWork = new UnitOfWork(_context, mapperConfig.CreateMapper(), null!);
        _client = Substitute.For<IDriveThruRpgClient>();
        _coverDbService = Substitute.For<ICoverDbService>();
        _eventHub = Substitute.For<IEventHub>();
        _service = new DriveThruRpgMetadataService(unitOfWork, _client, _coverDbService, _eventHub,
            NullLogger<DriveThruRpgMetadataService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Volume> CreatePublication(RpgMaterialType type = RpgMaterialType.CoreManual, string name = "Frontier Scum")
    {
        var volume = new VolumeBuilder(name).WithSeriesId(_seriesId).Build();
        volume.RpgMaterialType = type;
        _context.Volume.Add(volume);
        await _context.SaveChangesAsync();
        return volume;
    }

    private async Task<Volume> Reload(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.Volume.AsNoTracking().SingleAsync(volume => volume.Id == id);
    }

    [Fact]
    public async Task Unique_exact_match_applies_unlocked_bibliography_and_preserves_locked_fields()
    {
        var volume = await CreatePublication();
        volume.Summary = "Local summary";
        volume.SummaryLocked = true;
        volume.RpgPublicationYear = 2021;
        volume.RpgPublicationYearLocked = true;
        volume.RpgWriters = ["Local writer"];
        volume.RpgWritersLocked = true;
        var chapter = new ChapterBuilder("0").Build();
        chapter.Language = "es";
        chapter.LanguageLocked = true;
        volume.Chapters.Add(chapter);
        await _context.SaveChangesAsync();

        _client.SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>())
            .Returns(new[] { new DriveThruRpgSearchResult(399483, "Frontier Scum") });
        _client.GetProductAsync(399483, Arg.Any<CancellationToken>())
            .Returns(new DriveThruRpgProduct(399483, "Frontier Scum: Core Rulebook", ["External author"],
                "Provider summary", "Publisher One", new DateTime(2024, 2, 3, 0, 0, 0, DateTimeKind.Utc), null, "en"));

        await _service.MatchUnmatchedVolumesAsync([volume.Id]);

        var saved = await Reload(volume.Id);
        Assert.Equal(399483, saved.DriveThruRpgId);
        Assert.Equal(DriveThruRpgMatchStatus.Linked, saved.DriveThruRpgMatchStatus);
        Assert.Equal("Frontier Scum: Core Rulebook", saved.Name);
        Assert.Equal("Local summary", saved.Summary);
        Assert.Equal(2021, saved.RpgPublicationYear);
        Assert.Equal(new[] { "Local writer" }, saved.RpgWriters);
        Assert.Equal(new[] { "Publisher One" }, saved.RpgPublishers);
        Assert.Equal("es", await _context.Chapter.Where(item => item.VolumeId == volume.Id)
            .Select(item => item.Language).SingleAsync());
    }

    [Fact]
    public async Task Ambiguous_exact_matches_are_not_linked_or_applied()
    {
        var volume = await CreatePublication();
        _client.SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new DriveThruRpgSearchResult(111, "Frontier Scum"),
                new DriveThruRpgSearchResult(222, "Frontier-Scum")
            });

        await _service.MatchUnmatchedVolumesAsync([volume.Id]);

        var saved = await Reload(volume.Id);
        Assert.Null(saved.DriveThruRpgId);
        Assert.Equal(DriveThruRpgMatchStatus.Ambiguous, saved.DriveThruRpgMatchStatus);
        await _client.DidNotReceive().GetProductAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provider_disabled_or_resource_volumes_are_never_searched()
    {
        var resource = await CreatePublication(RpgMaterialType.Map, "Pirate Borg Map");
        var publication = await CreatePublication();
        var library = await _context.Library.SingleAsync(item => item.Id == _libraryId);
        library.EnableDriveThruRpgMetadata = false;
        await _context.SaveChangesAsync();

        var result = await _service.SearchCandidatesForVolumeAsync(publication.Id);
        await _service.MatchUnmatchedVolumesAsync([resource.Id, publication.Id]);

        Assert.False(result.Succeeded);
        Assert.Equal(DriveThruRpgMetadataOperationError.ProviderDisabled, result.Error);
        await _client.DidNotReceive().SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(DriveThruRpgMatchStatus.NotSearched, (await Reload(resource.Id)).DriveThruRpgMatchStatus);
    }

    [Fact]
    public async Task Classified_resources_are_ineligible_while_the_provider_is_enabled()
    {
        var resource = await CreatePublication(RpgMaterialType.Map, "Pirate Borg Map");

        var result = await _service.SearchCandidatesForVolumeAsync(resource.Id);
        await _service.MatchUnmatchedVolumesAsync([resource.Id]);

        Assert.False(result.Succeeded);
        Assert.Equal(DriveThruRpgMetadataOperationError.NotPublication, result.Error);
        await _client.DidNotReceive().SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(DriveThruRpgMatchStatus.NotSearched, (await Reload(resource.Id)).DriveThruRpgMatchStatus);
    }

    [Fact]
    public async Task Candidate_search_never_links_or_fetches_product_details()
    {
        var volume = await CreatePublication();
        _client.SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new DriveThruRpgSearchResult(111, "Frontier Scum"),
                new DriveThruRpgSearchResult(222, "Frontier Scum Deluxe")
            });

        var result = await _service.SearchCandidatesForVolumeAsync(volume.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Null((await Reload(volume.Id)).DriveThruRpgId);
        await _client.DidNotReceive().GetProductAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Explicit_link_queues_product_for_refresh_and_respects_title_lock()
    {
        var volume = await CreatePublication();
        volume.Name = "My local title";
        volume.NameLocked = true;
        await _context.SaveChangesAsync();
        _client.GetProductAsync(1357, Arg.Any<CancellationToken>())
            .Returns(new DriveThruRpgProduct(1357, "Provider title", [], "Description", null,
                null, null, null));

        var link = await _service.LinkVolumeAsync(volume.Id, 1357);
        await _service.RefreshVolumeAsync(volume.Id);

        Assert.True(link.Succeeded);
        var saved = await Reload(volume.Id);
        Assert.Equal(1357, saved.DriveThruRpgId);
        Assert.Equal("My local title", saved.Name);
        Assert.Equal("Description", saved.Summary);
        Assert.Equal(DriveThruRpgMatchStatus.Linked, saved.DriveThruRpgMatchStatus);
    }

    [Fact]
    public async Task Provider_failure_is_recorded_without_creating_a_link()
    {
        var volume = await CreatePublication();
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("offline"));

        await _service.MatchUnmatchedVolumesAsync([volume.Id]);

        var saved = await Reload(volume.Id);
        Assert.Null(saved.DriveThruRpgId);
        Assert.Equal(DriveThruRpgMatchStatus.Failed, saved.DriveThruRpgMatchStatus);
        Assert.NotNull(saved.DriveThruRpgLastCheckedUtc);
    }
}
