using System.Collections.Generic;
using System.Threading.Tasks;
using Kavita.API.Services.Metadata;
using Kavita.Models.Builders;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Builders;
using Kavita.Services.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Kavita.Database.Tests.Entities;

public class RpgMaterialClassificationServiceTests(ITestOutputHelper output) : AbstractDbTest(output)
{
    [Fact]
    public async Task ClassifyBatch_SavesPublicationAndResourceTypes_AndQueuesOnlyUnlinkedPublications()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var publication = new VolumeBuilder("0").Build();
        var resource = new VolumeBuilder("1").Build();
        var series = new SeriesBuilder("Pirate Borg")
            .WithVolumes([publication, resource])
            .Build();
        var library = new LibraryBuilder("RPG", LibraryType.Rpg)
            .WithSeries(series)
            .Build();
        library.EnableRpgGeekMetadata = true;
        context.Library.Add(library);
        await unitOfWork.CommitAsync();

        var service = new RpgMaterialClassificationService(unitOfWork, NullLogger<RpgMaterialClassificationService>.Instance);
        var result = await service.ClassifyBatchAsync(series.Id,
        [
            new RpgMaterialClassificationUpdate(publication.Id, RpgMaterialType.Adventure),
            new RpgMaterialClassificationUpdate(resource.Id, RpgMaterialType.CardsAndTokens),
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal([publication.Id], result.RpgGeekSearchVolumeIds);
        Assert.Equal(RpgMaterialType.Adventure, publication.RpgMaterialType);
        Assert.Equal(RpgMaterialType.CardsAndTokens, resource.RpgMaterialType);
        Assert.Equal(RpgGeekMatchStatus.Pending, publication.RpgGeekMatchStatus);
        Assert.Equal(RpgGeekMatchStatus.NotSearched, resource.RpgGeekMatchStatus);
        Assert.Equal(DriveThruRpgMatchStatus.NotSearched, resource.DriveThruRpgMatchStatus);
    }

    [Fact]
    public async Task ClassifyBatch_IsAtomic_WhenAnEntryHasAnUndefinedType()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var publication = new VolumeBuilder("0").Build();
        var series = new SeriesBuilder("Heart").WithVolume(publication).Build();
        context.Library.Add(new LibraryBuilder("RPG", LibraryType.Rpg).WithSeries(series).Build());
        await unitOfWork.CommitAsync();

        var service = new RpgMaterialClassificationService(unitOfWork, NullLogger<RpgMaterialClassificationService>.Instance);
        var result = await service.ClassifyBatchAsync(series.Id,
        [
            new RpgMaterialClassificationUpdate(publication.Id, RpgMaterialType.Manual),
            new RpgMaterialClassificationUpdate(publication.Id + 999, (RpgMaterialType)999),
        ]);

        Assert.False(result.Succeeded);
        Assert.Equal(RpgMaterialClassificationError.InvalidType, result.Error);
        Assert.Equal(RpgMaterialType.Unclassified, publication.RpgMaterialType);
    }

    [Fact]
    public async Task ClassifyBatch_RejectsDuplicateIdsWithoutChangingAnyItem()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var publication = new VolumeBuilder("0").Build();
        var series = new SeriesBuilder("Heart").WithVolume(publication).Build();
        context.Library.Add(new LibraryBuilder("RPG", LibraryType.Rpg).WithSeries(series).Build());
        await unitOfWork.CommitAsync();

        var service = new RpgMaterialClassificationService(unitOfWork, NullLogger<RpgMaterialClassificationService>.Instance);
        var result = await service.ClassifyBatchAsync(series.Id,
        [
            new RpgMaterialClassificationUpdate(publication.Id, RpgMaterialType.Manual),
            new RpgMaterialClassificationUpdate(publication.Id, RpgMaterialType.Adventure),
        ]);

        Assert.False(result.Succeeded);
        Assert.Equal(RpgMaterialClassificationError.DuplicateIds, result.Error);
        Assert.Equal(RpgMaterialType.Unclassified, publication.RpgMaterialType);
    }

    [Fact]
    public async Task ClassifyBatch_DoesNotQueueWhenProviderIsDisabled_AndPreservesExistingBibliographyLocksAndIds()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var publication = new VolumeBuilder("0").Build();
        publication.Name = "Terror in Tortuga — Free RPG Day 2026";
        publication.NameLocked = true;
        publication.CoverImage = "cover.jpg";
        publication.CoverImageLocked = true;
        publication.DriveThruRpgId = 81745;
        publication.DriveThruRpgMatchStatus = DriveThruRpgMatchStatus.Linked;
        publication.RpgGeekId = 290374;
        publication.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;
        var series = new SeriesBuilder("Pirate Borg").WithVolume(publication).Build();
        context.Library.Add(new LibraryBuilder("RPG", LibraryType.Rpg).WithSeries(series).Build());
        await unitOfWork.CommitAsync();

        var service = new RpgMaterialClassificationService(unitOfWork, NullLogger<RpgMaterialClassificationService>.Instance);
        var result = await service.ClassifyBatchAsync(series.Id,
        [new RpgMaterialClassificationUpdate(publication.Id, RpgMaterialType.Adventure)]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.RpgGeekSearchVolumeIds);
        Assert.Equal("Terror in Tortuga — Free RPG Day 2026", publication.Name);
        Assert.True(publication.NameLocked);
        Assert.Equal("cover.jpg", publication.CoverImage);
        Assert.True(publication.CoverImageLocked);
        Assert.Equal(81745, publication.DriveThruRpgId);
        Assert.Equal(290374, publication.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.Linked, publication.RpgGeekMatchStatus);
    }
}
