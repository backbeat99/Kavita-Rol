using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kavita.Models.Entities.Enums;

namespace Kavita.API.Services.Metadata;

public enum DriveThruRpgMetadataOperationError
{
    None = 0,
    VolumeNotFound = 1,
    NotRpgLibrary = 2,
    NotPublication = 3,
    ProviderDisabled = 4,
    QueryRequired = 5,
    InvalidProductId = 6,
    ProviderRequestFailed = 7,
}

public sealed record DriveThruRpgCandidateSearchResult(
    bool Succeeded,
    DriveThruRpgMetadataOperationError Error,
    DriveThruRpgMatchStatus Status,
    IReadOnlyList<DriveThruRpgSearchResult> Candidates);

public sealed record DriveThruRpgMetadataOperationResult(
    bool Succeeded,
    DriveThruRpgMetadataOperationError Error);

public interface IDriveThruRpgMetadataService
{
    Task MatchUnmatchedVolumesInLibraryAsync(int libraryId, CancellationToken cancellationToken = default);
    Task MatchUnmatchedVolumesAsync(IList<int> volumeIds, CancellationToken cancellationToken = default);
    Task<DriveThruRpgCandidateSearchResult> SearchCandidatesForVolumeAsync(
        int volumeId, string? query = null, CancellationToken cancellationToken = default);
    Task<DriveThruRpgMetadataOperationResult> LinkVolumeAsync(
        int volumeId, int productId, CancellationToken cancellationToken = default);
    Task<DriveThruRpgMetadataOperationResult> RefreshVolumeAsync(int volumeId, CancellationToken cancellationToken = default);
}
