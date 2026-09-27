using System;
using Kavita.Models.DTOs.Common;
using Kavita.Models.Entities.Enums;

namespace Kavita.Models.DTOs;

public sealed record UpdateVolumeDto : IUpdateExternalMetadataIds
{
    public int Id { get; init; }

    public int? AniListId { get; set; }
    public long? MalId { get; set; }
    public int? HardcoverId { get; set; }
    public long? MetronId { get; set; }
    public string ComicVineId { get; set; }
    public int? MangaBakaId { get; set; }
    public int? CbrId { get; set; }
    public int? DriveThruRpgId { get; set; }
    public int? RpgGeekId { get; set; }
    public RpgMaterialType? RpgMaterialType { get; set; }
    public bool? NameLocked { get; set; }
    public string? Summary { get; set; }
    public bool? SummaryLocked { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public bool? ReleaseDateLocked { get; set; }
    public string? Language { get; set; }
    public bool? LanguageLocked { get; set; }
}
