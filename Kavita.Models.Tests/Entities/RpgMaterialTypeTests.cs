using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;

namespace Kavita.Models.Tests.Entities;

public class RpgMaterialTypeTests
{
    [Theory]
    [InlineData(RpgMaterialType.CoreManual)]
    [InlineData(RpgMaterialType.Manual)]
    [InlineData(RpgMaterialType.Adventure)]
    [InlineData(RpgMaterialType.Supplement)]
    [InlineData(RpgMaterialType.OtherPublication)]
    public void PublicationTypes_AreEligibleForPublicationWorkflows(RpgMaterialType type)
    {
        Assert.True(type.IsPublication());
        Assert.False(type.IsResource());
    }

    [Theory]
    [InlineData(RpgMaterialType.Map)]
    [InlineData(RpgMaterialType.CharacterSheet)]
    [InlineData(RpgMaterialType.GameAid)]
    [InlineData(RpgMaterialType.CardsAndTokens)]
    [InlineData(RpgMaterialType.OtherResource)]
    public void Resources_AreNotEligibleForPublicationWorkflows(RpgMaterialType type)
    {
        Assert.False(type.IsPublication());
        Assert.True(type.IsResource());
    }

    [Fact]
    public void NewCatalogMaterial_RemainsUnclassifiedUntilTheUserChooses()
    {
        var material = new Volume { Name = "Interactive", MinNumber = 0, MaxNumber = 0 };

        Assert.Equal(RpgMaterialType.Unclassified, material.RpgMaterialType);
        Assert.False(material.RpgMaterialType.IsPublication());
        Assert.False(material.RpgMaterialType.IsResource());
    }

    [Fact]
    public void RpgLibraryType_IsAddedWithoutRenumberingExistingLibraryTypes()
    {
        Assert.Equal(0, (int)LibraryType.Manga);
        Assert.Equal(1, (int)LibraryType.Comic);
        Assert.Equal(2, (int)LibraryType.Book);
        Assert.Equal(3, (int)LibraryType.Image);
        Assert.Equal(4, (int)LibraryType.LightNovel);
        Assert.Equal(5, (int)LibraryType.ComicVine);
        Assert.Equal(6, (int)LibraryType.Rpg);
    }

    [Fact]
    public void ExternalRpgProviders_AreDisabledByDefaultForEveryLibrary()
    {
        var library = new Library { Name = "Test", MetadataProvider = MetadataProvider.Mangabaka };

        Assert.False(library.EnableDriveThruRpgMetadata);
        Assert.False(library.EnableRpgGeekMetadata);
    }

    [Fact]
    public void RpgGeekStates_AreDistinctAndLinkedIsAnExplicitTerminalState()
    {
        var states = Enum.GetValues<RpgGeekMatchStatus>();

        Assert.Equal(7, states.Length);
        Assert.Equal(7, states.Select(value => (int)value).Distinct().Count());
        Assert.Equal(RpgGeekMatchStatus.NotSearched, (RpgGeekMatchStatus)0);
        Assert.Equal(RpgGeekMatchStatus.Pending, (RpgGeekMatchStatus)1);
        Assert.Equal(RpgGeekMatchStatus.Candidate, (RpgGeekMatchStatus)2);
        Assert.Equal(RpgGeekMatchStatus.NoMatch, (RpgGeekMatchStatus)3);
        Assert.Equal(RpgGeekMatchStatus.Ambiguous, (RpgGeekMatchStatus)4);
        Assert.Equal(RpgGeekMatchStatus.Failed, (RpgGeekMatchStatus)5);
        Assert.Equal(RpgGeekMatchStatus.Linked, (RpgGeekMatchStatus)6);
    }
}
