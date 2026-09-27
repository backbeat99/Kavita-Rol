using System.Collections.Generic;

namespace Kavita.Models.DTOs;

/// <summary>Local, manually edited bibliography. External provider IDs are deliberately not part of this request.</summary>
public sealed record UpdateRpgBibliographyDto
{
    public string? Name { get; init; }
    public bool NameLocked { get; init; }
    public string? Summary { get; init; }
    public bool SummaryLocked { get; init; }
    public int? RpgPublicationYear { get; init; }
    public bool RpgPublicationYearLocked { get; init; }
    public List<string>? RpgWriters { get; init; }
    public bool RpgWritersLocked { get; init; }
    public List<string>? RpgPublishers { get; init; }
    public bool RpgPublishersLocked { get; init; }
}
