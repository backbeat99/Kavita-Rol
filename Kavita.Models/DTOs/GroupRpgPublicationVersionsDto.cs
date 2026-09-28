using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Kavita.Models.DTOs;

public sealed record GroupRpgPublicationVersionsDto
{
    [Range(1, int.MaxValue)]
    public int SeriesId { get; init; }

    [Range(1, int.MaxValue)]
    public int PrimaryVolumeId { get; init; }

    [Required]
    [MinLength(2)]
    public required IReadOnlyCollection<int> VolumeIds { get; init; }
}
