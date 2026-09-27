using System.Threading.Tasks;
using Kavita.Models.Builders;
using Kavita.Models.DTOs;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Builders;
using Kavita.Services.Metadata;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Kavita.Database.Tests.Entities;

public class RpgBibliographyPersistenceTests(ITestOutputHelper output) : AbstractDbTest(output)
{
    [Fact]
    public async Task ManualBibliographyPersistsWithLockedTitleAndNoProviderLink()
    {
        var (unitOfWork, context, _) = await CreateDatabase();
        var volume = new VolumeBuilder("Core Book").Build();
        volume.RpgMaterialType = RpgMaterialType.CoreManual;
        context.Library.Add(new LibraryBuilder("Roleplaying", LibraryType.Rpg)
            .WithSeries(new SeriesBuilder("Pirate Borg").WithVolume(volume).Build()).Build());
        await unitOfWork.CommitAsync();

        Assert.True(RpgBibliographyEditor.TryApply(volume, new UpdateRpgBibliographyDto
        {
            Name = "Pirate Borg Core Book", NameLocked = true,
            Summary = "A game of pirates", SummaryLocked = true,
            RpgPublicationYear = 2022, RpgPublicationYearLocked = true,
            RpgWriters = ["Author"], RpgWritersLocked = true,
            RpgPublishers = ["Publisher"], RpgPublishersLocked = true
        }));
        await unitOfWork.CommitAsync();
        context.ChangeTracker.Clear();

        var saved = await context.Volume.SingleAsync(item => item.Id == volume.Id);
        Assert.Equal("Pirate Borg Core Book", saved.Name);
        Assert.True(saved.NameLocked);
        Assert.Equal("A game of pirates", saved.Summary);
        Assert.Equal(2022, saved.RpgPublicationYear);
        Assert.Equal(["Author"], saved.RpgWriters);
        Assert.Equal(["Publisher"], saved.RpgPublishers);
        Assert.Null(saved.RpgGeekId);
    }
}
