using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Hangfire;
using Kavita.API.Database;
using Kavita.API.Services;
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
using Xunit;

namespace Kavita.Services.Tests.Metadata;

public class DriveThruRpgVolumeMatchTests
{
    private readonly SqliteConnection _connection;
    private readonly DataContext _context;
    private readonly IDriveThruRpgClient _client;
    private readonly DriveThruRpgMetadataService _service;
    private readonly int _volumeId;

    public DriveThruRpgVolumeMatchTests()
    {
        GlobalConfiguration.Configuration.UseInMemoryStorage();
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DataContext>().UseSqlite(_connection).Options;
        _context = new DataContext(options);
        _context.Database.EnsureCreatedAsync().GetAwaiter().GetResult();

        var library = new LibraryBuilder("Rol").Build();
        library.Type = LibraryType.Rpg;
        library.EnableDriveThruRpgMetadata = true;
        _context.Library.Add(library);
        _context.SaveChanges();

        var series = new SeriesBuilder("Pirate Borg").WithLibraryId(library.Id).Build();
        _context.Series.Add(series);
        _context.SaveChanges();

        var chapter = new ChapterBuilder("0").Build();
        chapter.IsSpecial = true;
        chapter.Title = "PDF (Pages)";
        var volume = new VolumeBuilder("Core Book").WithChapter(chapter).WithSeriesId(series.Id).Build();
        volume.RpgMaterialType = RpgMaterialType.CoreManual;
        _context.Volume.Add(volume);
        _context.SaveChanges();
        _volumeId = volume.Id;

        var config = new MapperConfiguration(cfg => cfg.AddMaps(typeof(AutoMapperProfiles).Assembly));
        var mapper = config.CreateMapper();
        var unitOfWork = new UnitOfWork(_context, mapper, null!);
        _client = Substitute.For<IDriveThruRpgClient>();
        var coverDbService = Substitute.For<ICoverDbService>();
        var eventHub = Substitute.For<IEventHub>();
        _service = new DriveThruRpgMetadataService(unitOfWork, _client, coverDbService, eventHub,
            NullLogger<DriveThruRpgMetadataService>.Instance);
    }

    private Volume ReloadVolume()
    {
        _context.ChangeTracker.Clear();
        return _context.Volume.AsNoTracking().Single(v => v.Id == _volumeId);
    }

    [Fact]
    public async Task Unique_match_marks_volume_linked_and_records_check_time()
    {
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<DriveThruRpgSearchResult> { new(399483, "Core Book") });
        _client.GetProductAsync(399483, Arg.Any<CancellationToken>())
            .Returns(new DriveThruRpgProduct(399483, "Pirate Borg Core Book", ["Luke Stratton"],
                "A pirate RPG", "Free League", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, "en"));

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume();
        Assert.Equal(399483, volume.DriveThruRpgId);
        Assert.Equal(RpgMaterialType.CoreManual, volume.RpgMaterialType);
        Assert.Equal(DriveThruRpgMatchStatus.Linked, volume.DriveThruRpgMatchStatus);
        Assert.NotNull(volume.DriveThruRpgLastCheckedUtc);
        Assert.Equal("Pirate Borg Core Book", volume.Name);
    }

    [Fact]
    public async Task Ambiguous_titles_are_left_unlinked_and_marked_ambiguous()
    {
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<DriveThruRpgSearchResult>
            {
                new(111, "Core Book"),
                new(222, "Core Book")
            });

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume();
        Assert.Null(volume.DriveThruRpgId);
        Assert.Equal(DriveThruRpgMatchStatus.Ambiguous, volume.DriveThruRpgMatchStatus);
        Assert.NotNull(volume.DriveThruRpgLastCheckedUtc);
        Assert.Equal("Core Book", volume.Name);
    }

    [Fact]
    public async Task No_results_marks_volume_no_match()
    {
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<DriveThruRpgSearchResult>());

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume();
        Assert.Null(volume.DriveThruRpgId);
        Assert.Equal(DriveThruRpgMatchStatus.NoMatch, volume.DriveThruRpgMatchStatus);
        Assert.NotNull(volume.DriveThruRpgLastCheckedUtc);
    }

    [Fact]
    public async Task Network_failure_marks_volume_failed()
    {
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("offline"));

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume();
        Assert.Null(volume.DriveThruRpgId);
        Assert.Equal(DriveThruRpgMatchStatus.Failed, volume.DriveThruRpgMatchStatus);
        Assert.NotNull(volume.DriveThruRpgLastCheckedUtc);
    }

    [Fact]
    public async Task Link_volume_sets_explicit_product_id()
    {
        var linked = await _service.LinkVolumeAsync(_volumeId, 399483, CancellationToken.None);

        Assert.True(linked);
        var volume = ReloadVolume();
        Assert.Equal(399483, volume.DriveThruRpgId);
        Assert.Equal(DriveThruRpgMatchStatus.Linked, volume.DriveThruRpgMatchStatus);
    }

    [Fact]
    public async Task Candidates_returns_unique_search_results_for_eligible_volume()
    {
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<DriveThruRpgSearchResult>
            {
                new(111, "Core Book"),
                new(222, "Core Book Deluxe"),
                new(111, "Core Book")
            });

        var candidates = await _service.GetCandidatesAsync(_volumeId, null, CancellationToken.None);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.ProductId == 111);
        Assert.Contains(candidates, c => c.ProductId == 222);
    }

    [Fact]
    public async Task Candidates_are_not_offered_for_resources()
    {
        _context.Volume.Single(v => v.Id == _volumeId).RpgMaterialType = RpgMaterialType.Map;
        await _context.SaveChangesAsync();

        var candidates = await _service.GetCandidatesAsync(_volumeId, null, CancellationToken.None);

        Assert.Empty(candidates);
        await _client.DidNotReceive().SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ResolveMatchStatus_maps_outcome()
    {
        Assert.Equal(DriveThruRpgMatchStatus.Linked,
            DriveThruRpgMetadataService.ResolveMatchStatus(isAmbiguous: false, hasProduct: true));
        Assert.Equal(DriveThruRpgMatchStatus.Ambiguous,
            DriveThruRpgMetadataService.ResolveMatchStatus(isAmbiguous: true, hasProduct: false));
        Assert.Equal(DriveThruRpgMatchStatus.NoMatch,
            DriveThruRpgMetadataService.ResolveMatchStatus(isAmbiguous: false, hasProduct: false));
    }

    [Fact]
    public void ApplyVolumeMetadataFields_respects_locks()
    {
        var releaseDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var volume = new Volume
        {
            Name = "Custom title",
            MinNumber = 0,
            MaxNumber = 0,
            Summary = "Custom summary",
            ReleaseDate = releaseDate,
            Language = "es",
            SummaryLocked = true,
            ReleaseDateLocked = true,
            LanguageLocked = true
        };
        var product = new DriveThruRpgProduct(123, "Product title", [], "Product summary", null,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, "en");

        DriveThruRpgMetadataService.ApplyVolumeMetadataFields(volume, product);

        Assert.Equal("Custom summary", volume.Summary);
        Assert.Equal(releaseDate, volume.ReleaseDate);
        Assert.Equal("es", volume.Language);
    }

    [Fact]
    public void ApplyVolumeMetadataFields_updates_unlocked_fields()
    {
        var volume = new Volume { Name = "Old title", MinNumber = 0, MaxNumber = 0, Summary = "Old summary", Language = "" };
        var releaseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var product = new DriveThruRpgProduct(456, "New title", [], "New summary", null, releaseDate, null, "en");

        DriveThruRpgMetadataService.ApplyVolumeMetadataFields(volume, product);

        Assert.Equal("New summary", volume.Summary);
        Assert.Equal(releaseDate, volume.ReleaseDate);
        Assert.Equal("en", volume.Language);
    }

    [Fact]
    public async Task Linked_match_keeps_locked_title()
    {
        _context.Volume.Single(v => v.Id == _volumeId).NameLocked = true;
        await _context.SaveChangesAsync();

        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<DriveThruRpgSearchResult> { new(399483, "Core Book") });
        _client.GetProductAsync(399483, Arg.Any<CancellationToken>())
            .Returns(new DriveThruRpgProduct(399483, "Pirate Borg Core Book", ["Luke Stratton"],
                "A pirate RPG", "Free League", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, "en"));

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume();
        Assert.Equal(399483, volume.DriveThruRpgId);
        Assert.Equal("Core Book", volume.Name);
        Assert.Equal("A pirate RPG", volume.Summary);
        Assert.NotNull(volume.ReleaseDate);
        Assert.Equal("en", volume.Language);
    }
}
