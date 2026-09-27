using System;
using System.Collections.Generic;
using System.Linq;
using Kavita.Models.DTOs;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;

namespace Kavita.Services.Metadata;

/// <summary>Validates all manual RPG metadata before changing the tracked volume.</summary>
public static class RpgBibliographyEditor
{
    public static bool TryApply(Volume volume, UpdateRpgBibliographyDto dto)
    {
        var name = dto.Name?.Trim();
        var summary = dto.Summary?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 500 || summary.Length > 10000 ||
            dto.RpgPublicationYear is < 1000 or > 9999 ||
            !TryNormalize(dto.RpgWriters, out var writers) ||
            !TryNormalize(dto.RpgPublishers, out var publishers)) return false;

        var titleChanged = !string.Equals(volume.Name, name, StringComparison.Ordinal);
        volume.Name = name;
        volume.NameLocked = dto.NameLocked;
        volume.Summary = summary;
        volume.SummaryLocked = dto.SummaryLocked;
        volume.RpgPublicationYear = dto.RpgPublicationYear;
        volume.RpgPublicationYearLocked = dto.RpgPublicationYearLocked;
        volume.RpgWriters = writers;
        volume.RpgWritersLocked = dto.RpgWritersLocked;
        volume.RpgPublishers = publishers;
        volume.RpgPublishersLocked = dto.RpgPublishersLocked;

        // A previous search was for a different title. Never unlink an explicitly confirmed match.
        if (titleChanged && !volume.RpgGeekId.HasValue)
        {
            volume.RpgGeekMatchStatus = RpgGeekMatchStatus.NotSearched;
            volume.RpgGeekLastCheckedUtc = null;
        }
        return true;
    }

    private static bool TryNormalize(List<string>? values, out IList<string> normalized)
    {
        normalized = [];
        if (values is null || values.Count > 50) return false;
        if (values.Any(value => value is null || value.Length > 200)) return false;
        normalized = values.Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }
}
