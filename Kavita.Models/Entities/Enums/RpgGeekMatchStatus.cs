namespace Kavita.Models.Entities.Enums;

/// <summary>Discovery status for a publication; only an explicit user confirmation creates a link.</summary>
public enum RpgGeekMatchStatus
{
    NotSearched = 0,
    Pending = 1,
    Candidate = 2,
    NoMatch = 3,
    Ambiguous = 4,
    Failed = 5,
    Linked = 6,
}
