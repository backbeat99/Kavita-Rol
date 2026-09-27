using System.ComponentModel;

namespace Kavita.Models.Entities.Enums;

/// <summary>Classification used only by RPG libraries to distinguish publications from play aids.</summary>
public enum RpgMaterialType
{
    [Description("Unclassified")]
    Unclassified = 0,
    [Description("Core Manual")]
    CoreManual = 1,
    [Description("Manual")]
    Manual = 2,
    [Description("Adventure")]
    Adventure = 3,
    [Description("Supplement")]
    Supplement = 4,
    [Description("Other Publication")]
    OtherPublication = 5,
    [Description("Map")]
    Map = 6,
    [Description("Character Sheet")]
    CharacterSheet = 7,
    [Description("Game Aid")]
    GameAid = 8,
    [Description("Cards and Tokens")]
    CardsAndTokens = 9,
    [Description("Other Resource")]
    OtherResource = 10,
}

public static class RpgMaterialTypeExtensions
{
    public static bool IsPublication(this RpgMaterialType type) => type is
        RpgMaterialType.CoreManual or RpgMaterialType.Manual or RpgMaterialType.Adventure or
        RpgMaterialType.Supplement or RpgMaterialType.OtherPublication;
}
