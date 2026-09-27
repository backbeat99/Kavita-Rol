using System.ComponentModel.DataAnnotations;

namespace Kavita.Models.DTOs;

public sealed record ApplyRpgGeekCandidateDto
{
    [Range(1, int.MaxValue)]
    public int VolumeId { get; init; }

    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Required]
    public required string PreviewFingerprint { get; init; }

    public bool ReplaceTitle { get; init; }
    public bool ReplaceSummary { get; init; }
    public bool ReplaceYear { get; init; }
    public bool ReplaceWriters { get; init; }
    public bool ReplacePublishers { get; init; }
    public bool ReplaceCover { get; init; }
}
