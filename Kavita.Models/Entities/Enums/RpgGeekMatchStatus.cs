namespace Kavita.Models.Entities.Enums;

/// <summary>Outcome of the last RPGGeek lookup for an RPG publication.</summary>
public enum RpgGeekMatchStatus
{
    NotSearched = 0,
    Linked = 1,
    NoMatch = 2,
    Ambiguous = 3,
    Failed = 4,
}
