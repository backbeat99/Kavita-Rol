using System.ComponentModel.DataAnnotations;

namespace Kavita.Models.DTOs;

public sealed record SplitRpgPublicationVersionDto
{
    [Range(1, int.MaxValue)]
    public int SeriesId { get; init; }

    [Range(1, int.MaxValue)]
    public int VolumeId { get; init; }

    [Range(1, int.MaxValue)]
    public int ChapterId { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Title { get; init; }
}
