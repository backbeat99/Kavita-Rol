using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Kavita.Models.DTOs;

public sealed record ApplyRpgGeekCandidatesBatchDto
{
    [Range(1, int.MaxValue)]
    public int SeriesId { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public required List<ApplyRpgGeekCandidateDto> Items { get; init; }
}
