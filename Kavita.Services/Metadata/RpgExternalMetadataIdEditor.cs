using Kavita.Models.DTOs;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;

namespace Kavita.Services.Metadata;

/// <summary>Applies explicitly entered RPG provider IDs without importing provider metadata.</summary>
public static class RpgExternalMetadataIdEditor
{
    public static bool TryApply(Volume volume, UpdateRpgExternalMetadataIdsDto dto)
    {
        if (dto.RpgGeekId is <= 0 || dto.DriveThruRpgId is <= 0) return false;

        if (volume.RpgGeekId != dto.RpgGeekId)
        {
            volume.RpgGeekId = dto.RpgGeekId;
            volume.RpgGeekMatchStatus = dto.RpgGeekId.HasValue
                ? RpgGeekMatchStatus.Linked
                : RpgGeekMatchStatus.NotSearched;
            volume.RpgGeekLastCheckedUtc = null;
        }

        if (volume.DriveThruRpgId != dto.DriveThruRpgId)
        {
            volume.DriveThruRpgId = dto.DriveThruRpgId;
            volume.DriveThruRpgMatchStatus = dto.DriveThruRpgId.HasValue
                ? DriveThruRpgMatchStatus.Linked
                : DriveThruRpgMatchStatus.NotSearched;
            volume.DriveThruRpgLastCheckedUtc = null;
        }

        return true;
    }
}
