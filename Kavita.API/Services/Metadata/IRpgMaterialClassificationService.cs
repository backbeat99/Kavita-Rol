using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kavita.Models.Entities.Enums;

namespace Kavita.API.Services.Metadata;

public sealed record RpgMaterialClassificationUpdate(int VolumeId, RpgMaterialType MaterialType);

public enum RpgMaterialClassificationError
{
    None = 0,
    EmptyBatch = 1,
    DuplicateIds = 2,
    InvalidType = 3,
    VolumeNotFound = 4,
    WrongSeries = 5,
    NotRpgLibrary = 6,
}

public sealed record RpgMaterialClassificationResult(
    bool Succeeded,
    RpgMaterialClassificationError Error,
    IReadOnlyList<int> RpgGeekSearchVolumeIds,
    IReadOnlyList<int> DriveThruRpgMatchVolumeIds);

public interface IRpgMaterialClassificationService
{
    Task<RpgMaterialClassificationResult> ClassifyBatchAsync(
        int seriesId,
        IReadOnlyCollection<RpgMaterialClassificationUpdate> updates,
        CancellationToken cancellationToken = default);
}
