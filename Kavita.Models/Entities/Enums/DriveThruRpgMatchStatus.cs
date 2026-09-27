namespace Kavita.Models.Entities.Enums;

/// <summary>Outcome of the last DriveThruRPG lookup for an RPG publication.</summary>
public enum DriveThruRpgMatchStatus
{
    NotSearched = 0,
    Linked = 1,
    NoMatch = 2,
    Ambiguous = 3,
    Failed = 4,
}
