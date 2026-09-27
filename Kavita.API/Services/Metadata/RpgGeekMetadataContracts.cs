using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kavita.Models.Entities.Enums;

namespace Kavita.API.Services.Metadata;

public sealed record RpgGeekSearchResult(int Id, string Name, int? YearPublished);

public sealed record RpgGeekProduct(
    int Id,
    string Title,
    int? YearPublished,
    string? Description,
    IReadOnlyList<string> Designers,
    IReadOnlyList<string> Publishers,
    string? ImageUrl);

public interface IRpgGeekClient
{
    bool IsEnabled { get; }
    Task<IReadOnlyList<RpgGeekSearchResult>> SearchProductsAsync(
        string query, CancellationToken cancellationToken = default, bool forceRefresh = false);
    Task<RpgGeekProduct?> GetProductAsync(
        int id, CancellationToken cancellationToken = default, bool forceRefresh = false);
}

public enum RpgGeekMetadataOperationError
{
    None = 0,
    VolumeNotFound = 1,
    NotRpgLibrary = 2,
    NotPublication = 3,
    ProviderDisabled = 4,
    TokenMissing = 5,
    QueryRequired = 6,
    ProductNotFound = 7,
    PreviewChanged = 8,
    ProviderRequestFailed = 9,
    BatchEmpty = 10,
    DuplicateVolumeIds = 11,
    WrongSeries = 12,
    AmbiguousRequiresIndividualReview = 13,
    CandidateNotReady = 14,
}

public sealed record RpgGeekCandidateSearchResult(
    bool Succeeded,
    RpgGeekMetadataOperationError Error,
    RpgGeekMatchStatus Status,
    IReadOnlyList<RpgGeekSearchResult> Candidates);

public sealed record RpgGeekCandidatePreview(RpgGeekProduct Product, string Fingerprint);

public sealed record RpgGeekCandidatePreviewResult(
    bool Succeeded,
    RpgGeekMetadataOperationError Error,
    RpgGeekCandidatePreview? Preview);

public sealed record RpgGeekCandidateApplyRequest(
    int VolumeId,
    int ProductId,
    string PreviewFingerprint,
    bool ReplaceTitle,
    bool ReplaceSummary,
    bool ReplaceYear,
    bool ReplaceWriters,
    bool ReplacePublishers,
    bool ReplaceCover);

public sealed record RpgGeekCandidateApplyResult(
    bool Succeeded,
    RpgGeekMetadataOperationError Error,
    RpgGeekCandidatePreview? UpdatedPreview);

public sealed record RpgGeekCandidateBatchPreview(int VolumeId, RpgGeekCandidatePreview Preview);

public sealed record RpgGeekCandidateBatchApplyResult(
    bool Succeeded,
    RpgGeekMetadataOperationError Error,
    IReadOnlyList<RpgGeekCandidateBatchPreview> UpdatedPreviews);
