using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kavita.API.Services.Metadata;

public interface IRpgGeekMetadataService
{
    /// <summary>Searches for candidates and updates only the discovery status, never links or applies metadata.</summary>
    Task SearchCandidatesAsync(int volumeId, CancellationToken cancellationToken = default);
    Task<RpgGeekCandidateSearchResult> SearchCandidatesForVolumeAsync(
        int volumeId, string? query = null, CancellationToken cancellationToken = default, bool forceRefresh = false);
    Task<RpgGeekCandidatePreviewResult> PreviewCandidateAsync(
        int volumeId, int productId, CancellationToken cancellationToken = default, bool forceRefresh = false);
    Task<RpgGeekCandidateApplyResult> ApplyCandidateAsync(
        RpgGeekCandidateApplyRequest request, CancellationToken cancellationToken = default);
    Task<RpgGeekCandidateBatchApplyResult> ApplyCandidatesBatchAsync(
        int seriesId, IReadOnlyList<RpgGeekCandidateApplyRequest> requests,
        CancellationToken cancellationToken = default);
}
