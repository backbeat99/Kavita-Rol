using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kavita.API.Database;
using Kavita.API.Services.Metadata;
using Kavita.Models.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kavita.Services.Metadata;

public sealed class RpgMaterialClassificationService(
    IUnitOfWork unitOfWork,
    ILogger<RpgMaterialClassificationService> logger) : IRpgMaterialClassificationService
{
    public async Task<RpgMaterialClassificationResult> ClassifyBatchAsync(
        int seriesId,
        IReadOnlyCollection<RpgMaterialClassificationUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
        {
            return Failure(RpgMaterialClassificationError.EmptyBatch);
        }

        var volumeIds = updates.Select(update => update.VolumeId).ToArray();
        if (volumeIds.Distinct().Count() != volumeIds.Length)
        {
            return Failure(RpgMaterialClassificationError.DuplicateIds);
        }

        if (updates.Any(update => !Enum.IsDefined(update.MaterialType)))
        {
            return Failure(RpgMaterialClassificationError.InvalidType);
        }

        var volumes = await unitOfWork.DataContext.Volume
            .Where(volume => volumeIds.Contains(volume.Id))
            .Include(volume => volume.Series)
            .ThenInclude(series => series.Library)
            .ToListAsync(cancellationToken);

        if (volumes.Count != updates.Count)
        {
            return Failure(RpgMaterialClassificationError.VolumeNotFound);
        }

        if (volumes.Any(volume => volume.SeriesId != seriesId))
        {
            return Failure(RpgMaterialClassificationError.WrongSeries);
        }

        if (volumes.Any(volume => volume.Series.Library.Type != LibraryType.Rpg))
        {
            return Failure(RpgMaterialClassificationError.NotRpgLibrary);
        }

        var typesById = updates.ToDictionary(update => update.VolumeId, update => update.MaterialType);
        var rpgGeekSearchVolumeIds = new List<int>();
        foreach (var volume in volumes)
        {
            var previousType = volume.RpgMaterialType;
            var nextType = typesById[volume.Id];
            volume.RpgMaterialType = nextType;

            if (!nextType.IsPublication())
            {
                ResetUnlinkedProviderSearch(volume);
                continue;
            }

            if (!volume.Series.Library.EnableRpgGeekMetadata || volume.RpgGeekId.HasValue)
            {
                continue;
            }

            var newlyClassifiedPublication = !previousType.IsPublication();
            if (!newlyClassifiedPublication && volume.RpgGeekMatchStatus != RpgGeekMatchStatus.NotSearched)
            {
                continue;
            }

            if (volume.RpgGeekMatchStatus == RpgGeekMatchStatus.Pending)
            {
                continue;
            }

            volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Pending;
            volume.RpgGeekLastCheckedUtc = null;
            rpgGeekSearchVolumeIds.Add(volume.Id);
        }

        if (unitOfWork.HasChanges() && !await unitOfWork.CommitAsync(cancellationToken))
        {
            logger.LogError("RPG batch classification for series {SeriesId} reported no database changes after updating items", seriesId);
        }

        return new RpgMaterialClassificationResult(true, RpgMaterialClassificationError.None, rpgGeekSearchVolumeIds);
    }

    private static void ResetUnlinkedProviderSearch(Kavita.Models.Entities.Volume volume)
    {
        if (volume.RpgGeekId is null)
        {
            volume.RpgGeekMatchStatus = RpgGeekMatchStatus.NotSearched;
            volume.RpgGeekLastCheckedUtc = null;
        }

        if (volume.DriveThruRpgId is null)
        {
            volume.DriveThruRpgMatchStatus = DriveThruRpgMatchStatus.NotSearched;
            volume.DriveThruRpgLastCheckedUtc = null;
        }
    }

    private static RpgMaterialClassificationResult Failure(RpgMaterialClassificationError error) =>
        new(false, error, Array.Empty<int>());
}
