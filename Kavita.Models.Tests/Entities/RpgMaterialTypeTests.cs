using Kavita.Models.Entities.Enums;

namespace Kavita.Models.Tests.Entities;

public class RpgMaterialTypeTests
{
    [Theory]
    [InlineData(RpgMaterialType.Unclassified, false)]
    [InlineData(RpgMaterialType.CoreManual, true)]
    [InlineData(RpgMaterialType.Manual, true)]
    [InlineData(RpgMaterialType.Adventure, true)]
    [InlineData(RpgMaterialType.Supplement, true)]
    [InlineData(RpgMaterialType.OtherPublication, true)]
    [InlineData(RpgMaterialType.Map, false)]
    [InlineData(RpgMaterialType.CharacterSheet, false)]
    [InlineData(RpgMaterialType.GameAid, false)]
    [InlineData(RpgMaterialType.CardsAndTokens, false)]
    [InlineData(RpgMaterialType.OtherResource, false)]
    public void Only_publications_are_eligible_for_product_metadata(RpgMaterialType type, bool expected)
    {
        Assert.Equal(expected, type.IsPublication());
    }

    [Fact]
    public void Unclassified_is_the_safe_default_for_existing_and_new_material()
    {
        Assert.Equal(0, (int)RpgMaterialType.Unclassified);
        Assert.Equal(RpgMaterialType.Unclassified, default(RpgMaterialType));
    }
}
