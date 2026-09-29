namespace Kavita.Models.DTOs;

/// <summary>Explicitly edited provider IDs for an RPG publication. Null clears the corresponding ID.</summary>
public sealed record UpdateRpgExternalMetadataIdsDto
{
    public int? RpgGeekId { get; init; }
    public int? DriveThruRpgId { get; init; }
}
