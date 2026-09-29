using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Kavita.API.Database;
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

public class RpgGeekMetadataServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private DataContext _context = null!;
    private IRpgGeekClient _client = null!;
    private ICoverDbService _coverDbService = null!;
    private IEventHub _eventHub = null!;
    private RpgGeekMetadataService _service = null!;
    private int _libraryId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DataContext>().UseSqlite(_connection).Options;
        _context = new DataContext(options);
        await _context.Database.EnsureCreatedAsync();

        var library = new LibraryBuilder("RPG").Build();
        library.Type = LibraryType.Rpg;
        library.EnableRpgGeekMetadata = true;
        _context.Library.Add(library);
        await _context.SaveChangesAsync();
        _libraryId = library.Id;

        var mapperConfig = new MapperConfiguration(config => config.AddMaps(typeof(AutoMapperProfiles).Assembly));
        var unitOfWork = new UnitOfWork(_context, mapperConfig.CreateMapper(), null!);
        _client = Substitute.For<IRpgGeekClient>();
        _client.IsEnabled.Returns(true);
        _coverDbService = Substitute.For<ICoverDbService>();
        _eventHub = Substitute.For<IEventHub>();
        _service = new RpgGeekMetadataService(unitOfWork, _client,
            _coverDbService, _eventHub,
            NullLogger<RpgGeekMetadataService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Volume> CreatePublication(RpgMaterialType materialType = RpgMaterialType.CoreManual)
    {
        var series = new SeriesBuilder("Test RPG").WithLibraryId(_libraryId).Build();
        _context.Series.Add(series);
        await _context.SaveChangesAsync();

        var volume = new VolumeBuilder("Frontier Scum").WithSeriesId(series.Id).Build();
        volume.RpgMaterialType = materialType;
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Pending;
        _context.Volume.Add(volume);
        await _context.SaveChangesAsync();
        return volume;
    }

    private async Task<Volume> CreatePublicationInSeries(int seriesId, string name)
    {
        var volume = new VolumeBuilder(name).WithSeriesId(seriesId).Build();
        volume.RpgMaterialType = RpgMaterialType.Adventure;
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Candidate;
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
    public async Task Candidate_is_saved_but_never_linked_or_applied_automatically()
    {
        var volume = await CreatePublication();
        _client.SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>())
            .Returns(new List<RpgGeekSearchResult> { new(370894, "Frontier Scum", 2022) });

        await _service.SearchCandidatesAsync(volume.Id);

        var saved = await Reload(volume.Id);
        Assert.Equal(RpgGeekMatchStatus.Candidate, saved.RpgGeekMatchStatus);
        Assert.Null(saved.RpgGeekId);
        Assert.Empty(saved.Summary);
        await _client.DidNotReceive().GetProductAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Multiple_candidates_are_marked_ambiguous_without_association()
    {
        var volume = await CreatePublication();
        _client.SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>())
            .Returns(new List<RpgGeekSearchResult>
            {
                new(370894, "Frontier Scum", 2022),
                new(370895, "Frontier Scum: Deluxe Edition", 2022),
            });

        await _service.SearchCandidatesAsync(volume.Id);

        var saved = await Reload(volume.Id);
        Assert.Equal(RpgGeekMatchStatus.Ambiguous, saved.RpgGeekMatchStatus);
        Assert.Null(saved.RpgGeekId);
    }

    [Fact]
    public async Task No_results_and_provider_failures_have_distinct_statuses()
    {
        var noMatch = await CreatePublication();
        _client.SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>())
            .Returns(new List<RpgGeekSearchResult>());
        await _service.SearchCandidatesAsync(noMatch.Id);
        Assert.Equal(RpgGeekMatchStatus.NoMatch, (await Reload(noMatch.Id)).RpgGeekMatchStatus);

        var failed = await CreatePublication();
        _client.SearchProductsAsync("Frontier Scum", Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("offline"));
        await _service.SearchCandidatesAsync(failed.Id);
        Assert.Equal(RpgGeekMatchStatus.Failed, (await Reload(failed.Id)).RpgGeekMatchStatus);
    }

    [Fact]
    public async Task Confirmation_applies_only_selected_unlocked_fields_and_preserves_version_language()
    {
        var volume = await CreatePublication();
        volume.Name = "Frontier Scum (ES)";
        volume.NameLocked = true;
        volume.Summary = "Local summary";
        volume.RpgPublicationYear = 2021;
        volume.RpgWriters = ["Local writer"];
        volume.CoverImage = "local-cover.webp";
        var chapter = new ChapterBuilder("0").Build();
        chapter.Language = "es";
        volume.Chapters.Add(chapter);
        await _context.SaveChangesAsync();

        var product = new RpgGeekProduct(370894, "Frontier Scum", 2022, "Provider summary",
            ["External designer"], ["External publisher"], "https://example.invalid/cover.jpg");
        _client.GetProductAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        var previewResult = await _service.PreviewCandidateAsync(volume.Id, product.Id);
        Assert.True(previewResult.Succeeded);
        var preview = Assert.IsType<RpgGeekCandidatePreview>(previewResult.Preview);

        var result = await _service.ApplyCandidateAsync(new RpgGeekCandidateApplyRequest(
            volume.Id, product.Id, preview.Fingerprint,
            ReplaceTitle: false, ReplaceSummary: false, ReplaceYear: false,
            ReplaceWriters: false, ReplacePublishers: false, ReplaceCover: false));

        Assert.True(result.Succeeded);
        var saved = await Reload(volume.Id);
        Assert.Equal(product.Id, saved.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.Linked, saved.RpgGeekMatchStatus);
        Assert.Equal("Frontier Scum (ES)", saved.Name);
        Assert.Equal("Local summary", saved.Summary);
        Assert.Equal(2021, saved.RpgPublicationYear);
        Assert.Equal(new[] { "Local writer" }, saved.RpgWriters);
        Assert.Equal(new[] { "External publisher" }, saved.RpgPublishers);
        Assert.Equal("es", await _context.Chapter.Where(item => item.VolumeId == volume.Id)
            .Select(item => item.Language).SingleAsync());
        await _coverDbService.DidNotReceive().SetVolumeCoverByUrl(
            Arg.Any<Volume>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirmation_replaces_an_existing_unlocked_publication_title_when_selected()
    {
        var volume = await CreatePublication();
        volume.Name = "Local filename";
        await _context.SaveChangesAsync();

        var product = new RpgGeekProduct(370894, "Frontier Scum", 2022, null, [], [], null);
        _client.GetProductAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        var preview = Assert.IsType<RpgGeekCandidatePreview>(
            (await _service.PreviewCandidateAsync(volume.Id, product.Id)).Preview);

        var result = await _service.ApplyCandidateAsync(new RpgGeekCandidateApplyRequest(
            volume.Id, product.Id, preview.Fingerprint,
            ReplaceTitle: true, ReplaceSummary: false, ReplaceYear: false,
            ReplaceWriters: false, ReplacePublishers: false, ReplaceCover: false));

        Assert.True(result.Succeeded);
        var saved = await Reload(volume.Id);
        Assert.Equal("Frontier Scum", saved.Name);
        Assert.Equal(product.Id, saved.RpgGeekId);
    }

    [Fact]
    public async Task Stale_preview_requires_confirmation_again_and_does_not_link()
    {
        var volume = await CreatePublication();
        var product = new RpgGeekProduct(370894, "Frontier Scum", 2022, "New summary", [], [], null);
        _client.GetProductAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _service.ApplyCandidateAsync(new RpgGeekCandidateApplyRequest(
            volume.Id, product.Id, "stale", false, false, false, false, false, false));

        Assert.False(result.Succeeded);
        Assert.Equal(RpgGeekMetadataOperationError.PreviewChanged, result.Error);
        Assert.Null((await Reload(volume.Id)).RpgGeekId);
    }

    [Fact]
    public async Task Batch_confirmation_links_each_selected_candidate_and_applies_empty_fields()
    {
        var first = await CreatePublication();
        first.RpgGeekMatchStatus = RpgGeekMatchStatus.Candidate;
        var second = await CreatePublicationInSeries(first.SeriesId, "Trapped in the Tropics");
        await _context.SaveChangesAsync();

        var firstProduct = new RpgGeekProduct(370894, "Frontier Scum", 2022, "An acid western RPG.",
            ["Karl Druid"], ["Games Omnivorous"], null);
        var secondProduct = new RpgGeekProduct(370895, "Trapped in the Tropics", 2020, "A Pirate Borg adventure.",
            ["Luca Rejec"], ["Limithron"], null);
        _client.GetProductAsync(firstProduct.Id, Arg.Any<CancellationToken>()).Returns(firstProduct);
        _client.GetProductAsync(secondProduct.Id, Arg.Any<CancellationToken>()).Returns(secondProduct);

        var firstPreview = Assert.IsType<RpgGeekCandidatePreview>(
            (await _service.PreviewCandidateAsync(first.Id, firstProduct.Id)).Preview);
        var secondPreview = Assert.IsType<RpgGeekCandidatePreview>(
            (await _service.PreviewCandidateAsync(second.Id, secondProduct.Id)).Preview);
        var result = await _service.ApplyCandidatesBatchAsync(first.SeriesId,
        [
            new RpgGeekCandidateApplyRequest(first.Id, firstProduct.Id, firstPreview.Fingerprint,
                false, false, false, false, false, false),
            new RpgGeekCandidateApplyRequest(second.Id, secondProduct.Id, secondPreview.Fingerprint,
                false, false, false, false, false, false)
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal(RpgGeekMetadataOperationError.None, result.Error);
        var savedFirst = await Reload(first.Id);
        Assert.Equal(firstProduct.Id, savedFirst.RpgGeekId);
        Assert.Equal(firstProduct.Title, savedFirst.Name);
        Assert.Equal(firstProduct.YearPublished, savedFirst.RpgPublicationYear);
        var savedSecond = await Reload(second.Id);
        Assert.Equal(secondProduct.Id, savedSecond.RpgGeekId);
        Assert.Equal(secondProduct.Description, savedSecond.Summary);
        Assert.Equal(new[] { "Luca Rejec" }, savedSecond.RpgWriters);
    }

    [Fact]
    public async Task Batch_confirmation_replaces_an_existing_unlocked_publication_title_when_selected()
    {
        var volume = await CreatePublication();
        volume.Name = "Local filename";
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Candidate;
        await _context.SaveChangesAsync();

        var product = new RpgGeekProduct(370894, "Frontier Scum", 2022, null, [], [], null);
        _client.GetProductAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        var preview = Assert.IsType<RpgGeekCandidatePreview>(
            (await _service.PreviewCandidateAsync(volume.Id, product.Id)).Preview);

        var result = await _service.ApplyCandidatesBatchAsync(volume.SeriesId,
        [
            new RpgGeekCandidateApplyRequest(volume.Id, product.Id, preview.Fingerprint,
                ReplaceTitle: true, ReplaceSummary: false, ReplaceYear: false,
                ReplaceWriters: false, ReplacePublishers: false, ReplaceCover: false)
        ]);

        Assert.True(result.Succeeded);
        var saved = await Reload(volume.Id);
        Assert.Equal("Frontier Scum", saved.Name);
        Assert.Equal(product.Id, saved.RpgGeekId);
    }

    [Fact]
    public async Task Stale_preview_in_a_batch_prevents_every_candidate_from_being_linked()
    {
        var first = await CreatePublication();
        first.RpgGeekMatchStatus = RpgGeekMatchStatus.Candidate;
        var second = await CreatePublicationInSeries(first.SeriesId, "Trapped in the Tropics");
        await _context.SaveChangesAsync();
        var firstProduct = new RpgGeekProduct(370894, "Frontier Scum", 2022, null, [], [], null);
        var secondProduct = new RpgGeekProduct(370895, "Trapped in the Tropics", 2020, null, [], [], null);
        _client.GetProductAsync(firstProduct.Id, Arg.Any<CancellationToken>()).Returns(firstProduct);
        _client.GetProductAsync(secondProduct.Id, Arg.Any<CancellationToken>()).Returns(secondProduct);

        var firstPreview = Assert.IsType<RpgGeekCandidatePreview>(
            (await _service.PreviewCandidateAsync(first.Id, firstProduct.Id)).Preview);
        var result = await _service.ApplyCandidatesBatchAsync(first.SeriesId,
        [
            new RpgGeekCandidateApplyRequest(first.Id, firstProduct.Id, firstPreview.Fingerprint,
                false, false, false, false, false, false),
            new RpgGeekCandidateApplyRequest(second.Id, secondProduct.Id, "stale-preview",
                false, false, false, false, false, false)
        ]);

        Assert.False(result.Succeeded);
        Assert.Equal(RpgGeekMetadataOperationError.PreviewChanged, result.Error);
        Assert.Equal(second.Id, Assert.Single(result.UpdatedPreviews).VolumeId);
        Assert.Null((await Reload(first.Id)).RpgGeekId);
        Assert.Null((await Reload(second.Id)).RpgGeekId);
    }

    [Fact]
    public async Task Ambiguous_candidates_are_rejected_from_batch_confirmation()
    {
        var volume = await CreatePublication();
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Ambiguous;
        await _context.SaveChangesAsync();

        var result = await _service.ApplyCandidatesBatchAsync(volume.SeriesId,
        [new RpgGeekCandidateApplyRequest(volume.Id, 370894, "fingerprint",
            false, false, false, false, false, false)]);

        Assert.False(result.Succeeded);
        Assert.Equal(RpgGeekMetadataOperationError.AmbiguousRequiresIndividualReview, result.Error);
        Assert.Empty(result.UpdatedPreviews);
        Assert.Null((await Reload(volume.Id)).RpgGeekId);
        await _client.DidNotReceive().GetProductAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_token_and_non_publications_do_not_make_provider_requests()
    {
        var resource = await CreatePublication(RpgMaterialType.CharacterSheet);
        _client.IsEnabled.Returns(false);
        await _service.SearchCandidatesAsync(resource.Id);
        await _client.DidNotReceive().SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        var publication = await CreatePublication();
        await _service.SearchCandidatesAsync(publication.Id);
        Assert.Equal(RpgGeekMatchStatus.Failed, (await Reload(publication.Id)).RpgGeekMatchStatus);
        await _client.DidNotReceive().SearchProductsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
