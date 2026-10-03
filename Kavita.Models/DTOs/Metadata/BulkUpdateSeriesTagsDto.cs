using System.Collections.Generic;

namespace Kavita.Models.DTOs.Metadata;

public sealed record BulkUpdateSeriesTagsDto
{
    public IList<int> SeriesIds { get; init; } = [];
    public IList<string> TagTitles { get; init; } = [];
    public bool Remove { get; init; }
}
