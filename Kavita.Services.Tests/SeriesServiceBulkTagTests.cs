using Kavita.API.Database;
using Kavita.API.Services;
using Kavita.API.Services.SignalR;
using Kavita.API.Services.ReadingLists;
using Kavita.Common;
using Kavita.Database.Tests;
using Kavita.Models.Builders;
using Kavita.Models.DTOs.Metadata;
using Kavita.Models.Entities.Enums;
using Kavita.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit.Abstractions;

namespace Kavita.Services.Tests;

public class SeriesServiceBulkTagTests(ITestOutputHelper outputHelper) : AbstractDbTest(outputHelper)
{
    [Fact]
    public async Task BulkUpdateSeriesTags_AddsTagsWithoutReplacingExistingOnes()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var existingTag = new TagBuilder("Existing").Build();
        var firstSeries = new SeriesBuilder("First")
            .WithFormat(MangaFormat.Archive)
            .WithMetadata(new SeriesMetadataBuilder().WithTag(existingTag).Build())
            .Build();
        var secondSeries = new SeriesBuilder("Second")
            .WithFormat(MangaFormat.Archive)
            .Build();
        unitOfWork.LibraryRepository.Add(new LibraryBuilder("Library", LibraryType.Manga)
            .WithSeries(firstSeries)
            .WithSeries(secondSeries)
            .Build());
        await unitOfWork.CommitAsync();

        var service = CreateService(unitOfWork);
        var result = await service.BulkUpdateSeriesTags(new BulkUpdateSeriesTagsDto
        {
            SeriesIds = [firstSeries.Id, secondSeries.Id],
            TagTitles = ["New tag", " new TAG "]
        });

        Assert.True(result);
        context.ChangeTracker.Clear();
        var metadata = await context.SeriesMetadata
            .Where(item => item.SeriesId == firstSeries.Id || item.SeriesId == secondSeries.Id)
            .Include(item => item.Tags)
            .ToDictionaryAsync(item => item.SeriesId);

        Assert.Contains(metadata[firstSeries.Id].Tags, tag => tag.NormalizedTitle == "existing");
        Assert.Contains(metadata[firstSeries.Id].Tags, tag => tag.NormalizedTitle == "newtag");
        Assert.Contains(metadata[secondSeries.Id].Tags, tag => tag.NormalizedTitle == "newtag");
        Assert.True(metadata[firstSeries.Id].TagsLocked);
        Assert.True(metadata[secondSeries.Id].TagsLocked);
        Assert.Single(await context.Tag.Where(tag => tag.NormalizedTitle == "newtag").ToListAsync());
    }

    [Fact]
    public async Task BulkUpdateSeriesTags_RemovesOnlySelectedTags()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var keepTag = new TagBuilder("Keep").Build();
        var removeTag = new TagBuilder("Remove").Build();
        var firstSeries = new SeriesBuilder("First")
            .WithFormat(MangaFormat.Archive)
            .WithMetadata(new SeriesMetadataBuilder().WithTag(keepTag).WithTag(removeTag).Build())
            .Build();
        var secondSeries = new SeriesBuilder("Second")
            .WithFormat(MangaFormat.Archive)
            .WithMetadata(new SeriesMetadataBuilder().WithTag(keepTag).Build())
            .Build();
        unitOfWork.LibraryRepository.Add(new LibraryBuilder("Library", LibraryType.Manga)
            .WithSeries(firstSeries)
            .WithSeries(secondSeries)
            .Build());
        await unitOfWork.CommitAsync();

        var service = CreateService(unitOfWork);
        var result = await service.BulkUpdateSeriesTags(new BulkUpdateSeriesTagsDto
        {
            SeriesIds = [firstSeries.Id, secondSeries.Id],
            TagTitles = ["Remove"],
            Remove = true
        });

        Assert.True(result);
        context.ChangeTracker.Clear();
        var metadata = await context.SeriesMetadata
            .Where(item => item.SeriesId == firstSeries.Id || item.SeriesId == secondSeries.Id)
            .Include(item => item.Tags)
            .ToDictionaryAsync(item => item.SeriesId);

        Assert.Single(metadata[firstSeries.Id].Tags);
        Assert.Contains(metadata[firstSeries.Id].Tags, tag => tag.NormalizedTitle == "keep");
        Assert.Single(metadata[secondSeries.Id].Tags);
        Assert.Contains(metadata[secondSeries.Id].Tags, tag => tag.NormalizedTitle == "keep");
        Assert.True(metadata[firstSeries.Id].TagsLocked);
        Assert.False(metadata[secondSeries.Id].TagsLocked);
    }

    private static SeriesService CreateService(IUnitOfWork unitOfWork)
    {
        return new SeriesService(
            unitOfWork,
            Substitute.For<IEventHub>(),
            Substitute.For<ITaskScheduler>(),
            Substitute.For<ILogger<SeriesService>>(),
            Substitute.For<ILocalizationService>(),
            Substitute.For<IReadingListService>(),
            Substitute.For<IEntityNamingService>());
    }
}
