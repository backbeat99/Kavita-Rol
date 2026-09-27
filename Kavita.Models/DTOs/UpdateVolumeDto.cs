using Kavita.Models.DTOs.Common;

namespace Kavita.Models.DTOs;

public sealed record UpdateVolumeDto : IUpdateExternalMetadataIds
{
    public int Id { get; init; }
    /// <summary>Only used for RPG volumes; omitted by existing clients.</summary>
    public UpdateRpgBibliographyDto? RpgBibliography { get; init; }

    public int? AniListId { get; set; }
    public long? MalId { get; set; }
    public int? HardcoverId { get; set; }
    public long? MetronId { get; set; }
    public string ComicVineId { get; set; }
    public int? MangaBakaId { get; set; }
    public int? CbrId { get; set; }
}
