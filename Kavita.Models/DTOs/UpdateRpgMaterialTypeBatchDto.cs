using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Kavita.Models.Entities.Enums;

namespace Kavita.Models.DTOs;

public sealed record UpdateRpgMaterialTypeBatchDto
{
    [Required]
    public int SeriesId { get; init; }

    [Required]
    [MinLength(1)]
    public required IReadOnlyCollection<RpgMaterialTypeUpdateDto> Items { get; init; }
}

public sealed record RpgMaterialTypeUpdateDto
{
    [Range(1, int.MaxValue)]
    public int VolumeId { get; init; }

    [EnumDataType(typeof(RpgMaterialType))]
    public RpgMaterialType MaterialType { get; init; }
}
