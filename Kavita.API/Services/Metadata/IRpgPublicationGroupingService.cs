using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kavita.API.Services.Metadata;

public enum RpgPublicationGroupingError
{
    None = 0,
    InvalidSelection = 1,
    DuplicateVolumeIds = 2,
    VolumeNotFound = 3,
    WrongSeries = 4,
    NotRpgLibrary = 5,
    ResourceSelected = 6,
    ConflictingMaterialTypes = 7,
    ConflictingProviderIds = 8,
    EmptyPublication = 9,
    SaveFailed = 10,
    ProviderIdentityNotPrimary = 11,
}

public sealed record RpgPublicationGroupingResult(
    bool Succeeded,
    RpgPublicationGroupingError Error,
    int PrimaryVolumeId,
    IReadOnlyList<int> RemovedVolumeIds);

public enum RpgPublicationVersionSplitError
{
    None = 0,
    VolumeNotFound = 1,
    WrongSeries = 2,
    NotRpgLibrary = 3,
    NotPublication = 4,
    VersionNotFound = 5,
    LastVersion = 6,
    InvalidTitle = 7,
    SaveFailed = 8,
}

public sealed record RpgPublicationVersionSplitResult(
    bool Succeeded,
    RpgPublicationVersionSplitError Error,
    int NewVolumeId);

public interface IRpgPublicationGroupingService
{
    Task<RpgPublicationGroupingResult> GroupVersionsAsync(
        int seriesId,
        int primaryVolumeId,
        IReadOnlyCollection<int> volumeIds,
        CancellationToken cancellationToken = default);

    Task<RpgPublicationVersionSplitResult> SplitVersionAsync(
        int seriesId,
        int volumeId,
        int chapterId,
        string title,
        CancellationToken cancellationToken = default);
}
