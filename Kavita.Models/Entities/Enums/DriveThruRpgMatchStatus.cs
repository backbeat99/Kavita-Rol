namespace Kavita.Models.Entities.Enums;

/// <summary>Discovery status for the independent DriveThruRPG provider.</summary>
public enum DriveThruRpgMatchStatus
{
    NotSearched = 0,
    Pending = 1,
    Candidate = 2,
    NoMatch = 3,
    Ambiguous = 4,
    Failed = 5,
    Linked = 6,
}
