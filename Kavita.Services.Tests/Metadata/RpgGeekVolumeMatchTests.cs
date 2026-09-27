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

public class RpgGeekVolumeMatchTests
{
    private readonly SqliteConnection _connection;
    private readonly DataContext _context;
    private readonly IRpgGeekClient _client;
    private readonly RpgGeekMetadataService _service;
    private readonly int _libraryId;
    private int _volumeId;

    public RpgGeekVolumeMatchTests()
    {
        GlobalConfiguration.Configuration.UseInMemoryStorage();
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DataContext>().UseSqlite(_connection).Options;
        _context = new DataContext(options);
        _context.Database.EnsureCreatedAsync().GetAwaiter().GetResult();

        var library = new LibraryBuilder("Rol").Build();
        library.Type = LibraryType.Rpg;
        library.EnableRpgGeekMetadata = true;
        _context.Library.Add(library);
        _context.SaveChanges();
        _libraryId = library.Id;

        var config = new MapperConfiguration(cfg => cfg.AddMaps(typeof(AutoMapperProfiles).Assembly));
        var mapper = config.CreateMapper();
        var unitOfWork = new UnitOfWork(_context, mapper, null!);
        _client = Substitute.For<IRpgGeekClient>();
        _client.IsEnabled.Returns(true);
        var coverDbService = Substitute.For<ICoverDbService>();
        var eventHub = Substitute.For<IEventHub>();
        _service = new RpgGeekMetadataService(unitOfWork, _client, coverDbService, eventHub,
            NullLogger<RpgGeekMetadataService>.Instance);
    }

    private async Task<int> CreateVolume(string name, RpgMaterialType type = RpgMaterialType.CoreManual, int? dtrpgId = null, string? seriesName = null)
    {
        var series = new SeriesBuilder(seriesName ?? name + " game").WithLibraryId(_libraryId).Build();
        _context.Series.Add(series);
        await _context.SaveChangesAsync();

        var chapter = new ChapterBuilder("0").Build();
        chapter.IsSpecial = true;
        chapter.Title = "PDF (Pages)";
        var volume = new VolumeBuilder(name).WithChapter(chapter).WithSeriesId(series.Id).Build();
        volume.RpgMaterialType = type;
        volume.DriveThruRpgId = dtrpgId;
        _context.Volume.Add(volume);
        await _context.SaveChangesAsync();
        return volume.Id;
    }

    private Volume ReloadVolume(int volumeId)
    {
        _context.ChangeTracker.Clear();
        return _context.Volume.AsNoTracking()
            .Include(v => v.Chapters).ThenInclude(c => c.People).ThenInclude(p => p.Person)
            .Single(v => v.Id == volumeId);
    }

    [Fact]
    public async Task Unique_match_links_and_fills_empty_fields()
    {
        _volumeId = await CreateVolume("Frontier Scum");
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<RpgGeekSearchResult> { new(370894, "Frontier Scum", 2022) });
        _client.GetProductAsync(370894, Arg.Any<CancellationToken>())
            .Returns(new RpgGeekProduct(370894, "Frontier Scum", 2022, "An acid western RPG.",
                ["Karl Druid"], ["Games Omnivorous"], "https://cf.geekdo-images.com/x.jpg"));

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume(_volumeId);
        Assert.Equal(370894, volume.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.Linked, volume.RpgGeekMatchStatus);
        Assert.NotNull(volume.RpgGeekLastCheckedUtc);
        Assert.Equal("An acid western RPG.", volume.Summary);
        Assert.Equal(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc), volume.ReleaseDate);
        Assert.Contains(volume.Chapters.SelectMany(c => c.People), p => p.Person.Name == "Karl Druid");
        Assert.Contains(volume.Chapters.SelectMany(c => c.People), p => p.Person.Name == "Games Omnivorous");
        Assert.Equal("Frontier Scum", volume.Name);
    }

    [Fact]
    public async Task Ambiguous_titles_are_marked_and_not_linked()
    {
        _volumeId = await CreateVolume("Heart");
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<RpgGeekSearchResult>
            {
                new(290374, "Heart", 2020),
                new(999, "Heart", 2015)
            });

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume(_volumeId);
        Assert.Null(volume.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.Ambiguous, volume.RpgGeekMatchStatus);
    }

    [Fact]
    public async Task No_results_marks_no_match()
    {
        _volumeId = await CreateVolume("Unknown Game");
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<RpgGeekSearchResult>());

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume(_volumeId);
        Assert.Null(volume.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.NoMatch, volume.RpgGeekMatchStatus);
        Assert.NotNull(volume.RpgGeekLastCheckedUtc);
    }

    [Fact]
    public async Task Network_failure_marks_failed()
    {
        _volumeId = await CreateVolume("Frontier Scum");
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("offline"));

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume(_volumeId);
        Assert.Equal(RpgGeekMatchStatus.Failed, volume.RpgGeekMatchStatus);
        Assert.NotNull(volume.RpgGeekLastCheckedUtc);
    }

    [Fact]
    public async Task Backfill_skips_drivethrurpg_volumes_and_resources()
    {
        var dtrpgVolume = await CreateVolume("Public Access", dtrpgId: 3982);
        var resource = await CreateVolume("PC Worksheet", RpgMaterialType.CharacterSheet);
        var gap = await CreateVolume("Frontier Scum");
        _client.SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<RpgGeekSearchResult> { new(370894, "Frontier Scum", 2022) });
        _client.GetProductAsync(370894, Arg.Any<CancellationToken>())
            .Returns(new RpgGeekProduct(370894, "Frontier Scum", 2022, "An acid western RPG.",
                [], [], null));

        await _service.MatchUnmatchedVolumesInLibraryAsync(_libraryId, CancellationToken.None);

        var gapVolume = ReloadVolume(gap);
        Assert.Equal(RpgGeekMatchStatus.Linked, gapVolume.RpgGeekMatchStatus);
        Assert.Equal(1, _client.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IRpgGeekClient.SearchProductsAsync)));
        await _client.Received(1).SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>());
        await _client.DidNotReceive().SearchProductsAsync("Public Access", Arg.Any<CancellationToken>());
        await _client.DidNotReceive().SearchProductsAsync("PC Worksheet", Arg.Any<CancellationToken>());

        Assert.Equal(RpgGeekMatchStatus.NotSearched, ReloadVolume(dtrpgVolume).RpgGeekMatchStatus);
        Assert.Equal(RpgGeekMatchStatus.NotSearched, ReloadVolume(resource).RpgGeekMatchStatus);
    }

    [Fact]
    public async Task Link_sets_explicit_id_without_title_search()
    {
        _volumeId = await CreateVolume("Heart");
        _client.GetProductAsync(290374, Arg.Any<CancellationToken>())
            .Returns(new RpgGeekProduct(290374, "Heart: The City Beneath", 2020, "Desc", [], [], null));

        Assert.True(await _service.LinkVolumeAsync(_volumeId, 290374, CancellationToken.None));

        var volume = ReloadVolume(_volumeId);
        Assert.Equal(290374, volume.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.Linked, volume.RpgGeekMatchStatus);
        await _client.DidNotReceive().SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Scalar_metadata_respects_locks_and_filled_fields()
    {
        var volume = new Volume
        {
            Name = "Heart",
            MinNumber = 0,
            MaxNumber = 0,
            Summary = "Already here",
            ReleaseDate = new DateTime(2021, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Language = "es"
        };
        var product = new RpgGeekProduct(290374, "Heart: The City Beneath", 2020, "New description", [], [], null);

        RpgGeekMetadataService.ApplyRpgGeekScalarMetadata(volume, product);

        Assert.Equal("Already here", volume.Summary);
        Assert.Equal(new DateTime(2021, 5, 1, 0, 0, 0, DateTimeKind.Utc), volume.ReleaseDate);
    }

    [Fact]
    public void FindTitleMatch_prefers_unique_prefix_when_no_exact_match()
    {
        var results = new List<RpgGeekSearchResult>
        {
            new(290374, "Heart: The City Beneath", 2020),
            new(313167, "Heart: Doors to Elsewhere", 2020)
        };

        var (match, ambiguous) = RpgGeekMetadataService.FindTitleMatch("Heart", results);

        Assert.Null(match);
        Assert.True(ambiguous);

        var single = new List<RpgGeekSearchResult> { new(290374, "Heart: The City Beneath", 2020) };
        (match, ambiguous) = RpgGeekMetadataService.FindTitleMatch("Heart", single);
        Assert.NotNull(match);
        Assert.Equal(290374, match.Id);
        Assert.False(ambiguous);
    }

    [Fact]
    public async Task Titles_that_miss_fall_back_to_the_game_name()
    {
        _volumeId = await CreateVolume("Acid Western Roleplaying", seriesName: "Frontier Scum");
        _context.ChangeTracker.Clear();

        _client.SearchProductsAsync("Acid Western Roleplaying", Arg.Any<CancellationToken>())
            .Returns(new List<RpgGeekSearchResult>());
        _client.SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>())
            .Returns(new List<RpgGeekSearchResult> { new(370894, "Frontier Scum", 2022) });
        _client.GetProductAsync(370894, Arg.Any<CancellationToken>())
            .Returns(new RpgGeekProduct(370894, "Frontier Scum", 2022, "An acid western RPG.",
                ["Karl Druid"], ["Games Omnivorous"], null));

        await _service.MatchUnmatchedVolumesAsync(new[] { _volumeId }, CancellationToken.None);

        var volume = ReloadVolume(_volumeId);
        Assert.Equal(370894, volume.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.Linked, volume.RpgGeekMatchStatus);
        await _client.Received(1).SearchProductsAsync("Acid Western Roleplaying", Arg.Any<CancellationToken>());
        await _client.Received(1).SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>());
    }
}
