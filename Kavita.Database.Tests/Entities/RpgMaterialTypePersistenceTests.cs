using System.Linq;
using System.Threading.Tasks;
using Kavita.Models.Builders;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Builders;
using Xunit;
using Xunit.Abstractions;

namespace Kavita.Database.Tests.Entities;

public class RpgMaterialTypePersistenceTests(ITestOutputHelper testOutputHelper) : AbstractDbTest(testOutputHelper)
{
    [Fact]
    public async Task Existing_volumes_default_to_unclassified()
    {
        var (_, context, _) = await CreateDatabase();
        var library = context.Library.First();

        context.Series.Add(new SeriesBuilder("RpgGame")
            .WithLibraryId(library.Id)
            .WithVolume(new VolumeBuilder("Core Rules").Build())
            .Build());
        await context.SaveChangesAsync();

        var volume = context.Volume.Single(v => v.Name == "Core Rules");
        Assert.Equal(RpgMaterialType.Unclassified, volume.RpgMaterialType);
        Assert.Equal(0, (int)volume.RpgMaterialType);
    }

    [Fact]
    public async Task Classification_is_persisted_and_read_back()
    {
        var (_, context, _) = await CreateDatabase();
        var library = context.Library.First();

        var volume = new VolumeBuilder("Isle of the Ancients").Build();
        context.Series.Add(new SeriesBuilder("RpgGame")
            .WithLibraryId(library.Id)
            .WithVolume(volume)
            .Build());

        volume.RpgMaterialType = RpgMaterialType.Adventure;
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        var reloaded = context.Volume.Single(v => v.Name == "Isle of the Ancients");
        Assert.Equal(RpgMaterialType.Adventure, reloaded.RpgMaterialType);
    }

    [Fact]
    public async Task Resource_classification_roundtrips_too()
    {
        var (_, context, _) = await CreateDatabase();
        var library = context.Library.First();

        var volume = new VolumeBuilder("PC Worksheet").Build();
        context.Series.Add(new SeriesBuilder("RpgGame")
            .WithLibraryId(library.Id)
            .WithVolume(volume)
            .Build());

        volume.RpgMaterialType = RpgMaterialType.CharacterSheet;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reloaded = context.Volume.Single(v => v.Name == "PC Worksheet");
        Assert.Equal(RpgMaterialType.CharacterSheet, reloaded.RpgMaterialType);
        Assert.False(reloaded.RpgMaterialType.IsPublication());
    }
}
