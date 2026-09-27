using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kavita.API.Database;
using Kavita.API.Services.Metadata;
using Kavita.API.Services.SignalR;
using Kavita.Models.DTOs.SignalR;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kavita.Services.Metadata;

/// <summary>Discovers candidates; a record is linked and its data applied only after user confirmation.</summary>
public sealed class RpgGeekMetadataService(
    IUnitOfWork unitOfWork,
    IRpgGeekClient client,
    ICoverDbService coverDbService,
    IEventHub eventHub,
    ILogger<RpgGeekMetadataService> logger) : IRpgGeekMetadataService
{
    public async Task SearchCandidatesAsync(int volumeId, CancellationToken cancellationToken = default)
    {
        await SearchCandidatesForVolumeAsync(volumeId, cancellationToken: cancellationToken);
    }

    public async Task<RpgGeekCandidateSearchResult> SearchCandidatesForVolumeAsync(
        int volumeId,
        string? query = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        var volume = await GetVolumeAsync(volumeId, cancellationToken);
        if (volume is null) return SearchFailure(RpgGeekMetadataOperationError.VolumeNotFound);
        var eligibilityError = GetEligibilityError(volume);
        if (eligibilityError != RpgGeekMetadataOperationError.None) return SearchFailure(eligibilityError);

        if (volume.RpgGeekId.HasValue)
        {
            return new RpgGeekCandidateSearchResult(true, RpgGeekMetadataOperationError.None,
                RpgGeekMatchStatus.Linked, Array.Empty<RpgGeekSearchResult>());
        }

        if (!client.IsEnabled)
        {
            Complete(volume, RpgGeekMatchStatus.Failed);
            await SaveAsync(cancellationToken);
            return SearchFailure(RpgGeekMetadataOperationError.TokenMissing);
        }

        var searchTerm = string.IsNullOrWhiteSpace(query) ? volume.Name : query.Trim();
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return SearchFailure(RpgGeekMetadataOperationError.QueryRequired);
        }

        try
        {
            var candidates = await client.SearchProductsAsync(searchTerm, cancellationToken, forceRefresh);
            var status = GetDiscoveryStatus(searchTerm, candidates);
            Complete(volume, status);
            await SaveAsync(cancellationToken);
            return new RpgGeekCandidateSearchResult(true, RpgGeekMetadataOperationError.None, status,
                candidates.Take(25).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Complete(volume, RpgGeekMatchStatus.Failed);
            await SaveAsync(cancellationToken);
            logger.LogWarning("RPGGeek candidate search failed for volume {VolumeId} ({ErrorType})",
                volumeId, exception.GetType().Name);
            return SearchFailure(RpgGeekMetadataOperationError.ProviderRequestFailed);
        }
    }

    public async Task<RpgGeekCandidatePreviewResult> PreviewCandidateAsync(
        int volumeId,
        int productId,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        var volume = await GetVolumeAsync(volumeId, cancellationToken);
        if (volume is null) return PreviewFailure(RpgGeekMetadataOperationError.VolumeNotFound);
        var eligibilityError = GetEligibilityError(volume);
        if (eligibilityError != RpgGeekMetadataOperationError.None) return PreviewFailure(eligibilityError);
        if (productId <= 0) return PreviewFailure(RpgGeekMetadataOperationError.ProductNotFound);
        if (!client.IsEnabled) return PreviewFailure(RpgGeekMetadataOperationError.TokenMissing);

        try
        {
            var product = await client.GetProductAsync(productId, cancellationToken, forceRefresh);
            return product is null
                ? PreviewFailure(RpgGeekMetadataOperationError.ProductNotFound)
                : new RpgGeekCandidatePreviewResult(true, RpgGeekMetadataOperationError.None,
                    new RpgGeekCandidatePreview(product, Fingerprint(product)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("RPGGeek candidate preview failed for volume {VolumeId} ({ErrorType})",
                volumeId, exception.GetType().Name);
            return PreviewFailure(RpgGeekMetadataOperationError.ProviderRequestFailed);
        }
    }

    public async Task<RpgGeekCandidateApplyResult> ApplyCandidateAsync(
        RpgGeekCandidateApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var volume = await GetVolumeAsync(request.VolumeId, cancellationToken);
        if (volume is null) return ApplyFailure(RpgGeekMetadataOperationError.VolumeNotFound);
        var eligibilityError = GetEligibilityError(volume);
        if (eligibilityError != RpgGeekMetadataOperationError.None) return ApplyFailure(eligibilityError);
        if (request.ProductId <= 0) return ApplyFailure(RpgGeekMetadataOperationError.ProductNotFound);
        if (!client.IsEnabled) return ApplyFailure(RpgGeekMetadataOperationError.TokenMissing);

        RpgGeekProduct? product;
        try
        {
            product = await client.GetProductAsync(request.ProductId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("RPGGeek candidate confirmation failed for volume {VolumeId} ({ErrorType})",
                request.VolumeId, exception.GetType().Name);
            return ApplyFailure(RpgGeekMetadataOperationError.ProviderRequestFailed);
        }

        if (product is null) return ApplyFailure(RpgGeekMetadataOperationError.ProductNotFound);
        var currentPreview = new RpgGeekCandidatePreview(product, Fingerprint(product));
        if (!string.Equals(request.PreviewFingerprint, currentPreview.Fingerprint, StringComparison.Ordinal))
        {
            return new RpgGeekCandidateApplyResult(false, RpgGeekMetadataOperationError.PreviewChanged, currentPreview);
        }

        ApplyConfirmedFields(volume, product, request);
        volume.RpgGeekId = product.Id;
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;
        volume.RpgGeekLastCheckedUtc = DateTime.UtcNow;
        unitOfWork.VolumeRepository.Update(volume);

        if (unitOfWork.HasChanges())
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }

        if (request.ReplaceCover || string.IsNullOrWhiteSpace(volume.CoverImage))
        {
            if (!volume.CoverImageLocked && !string.IsNullOrWhiteSpace(product.ImageUrl))
            {
                try
                {
                    await coverDbService.SetVolumeCoverByUrl(volume, product.ImageUrl,
                        fromBase64: false, chooseBetterImage: false, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning("RPGGeek cover could not be applied to volume {VolumeId} ({ErrorType})",
                        volume.Id, exception.GetType().Name);
                }
            }
        }

        await eventHub.SendMessageAsync(MessageFactory.SeriesUpdated,
            MessageFactory.SeriesUpdatedEvent(volume.SeriesId), false, cancellationToken);
        return new RpgGeekCandidateApplyResult(true, RpgGeekMetadataOperationError.None, null);
    }

    public static RpgGeekMatchStatus GetDiscoveryStatus(
        string query,
        IReadOnlyCollection<RpgGeekSearchResult> results)
    {
        if (results.Count == 0) return RpgGeekMatchStatus.NoMatch;
        if (results.Count == 1) return RpgGeekMatchStatus.Candidate;

        var normalizedQuery = Normalize(query);
        var relatedCount = results.Count(result =>
        {
            var name = Normalize(result.Name);
            return name == normalizedQuery || name.StartsWith(normalizedQuery, StringComparison.Ordinal) ||
                   normalizedQuery.StartsWith(name, StringComparison.Ordinal);
        });

        return relatedCount == 1 ? RpgGeekMatchStatus.Candidate : RpgGeekMatchStatus.Ambiguous;
    }

    public async Task<RpgGeekCandidateBatchApplyResult> ApplyCandidatesBatchAsync(
        int seriesId,
        IReadOnlyList<RpgGeekCandidateApplyRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0) return BatchApplyFailure(RpgGeekMetadataOperationError.BatchEmpty);
        var volumeIds = requests.Select(request => request.VolumeId).ToArray();
        if (volumeIds.Distinct().Count() != volumeIds.Length)
            return BatchApplyFailure(RpgGeekMetadataOperationError.DuplicateVolumeIds);
        if (!client.IsEnabled) return BatchApplyFailure(RpgGeekMetadataOperationError.TokenMissing);

        var volumes = await unitOfWork.DataContext.Volume
            .Include(volume => volume.Series)
            .ThenInclude(series => series.Library)
            .Where(volume => volumeIds.Contains(volume.Id))
            .ToListAsync(cancellationToken);
        if (volumes.Count != volumeIds.Length) return BatchApplyFailure(RpgGeekMetadataOperationError.VolumeNotFound);
        if (volumes.Any(volume => volume.SeriesId != seriesId))
            return BatchApplyFailure(RpgGeekMetadataOperationError.WrongSeries);

        var volumesById = volumes.ToDictionary(volume => volume.Id);
        foreach (var request in requests)
        {
            var volume = volumesById[request.VolumeId];
            var eligibilityError = GetEligibilityError(volume);
            if (eligibilityError != RpgGeekMetadataOperationError.None) return BatchApplyFailure(eligibilityError);
            if (volume.RpgGeekId.HasValue) return BatchApplyFailure(RpgGeekMetadataOperationError.CandidateNotReady);
            if (volume.RpgGeekMatchStatus == RpgGeekMatchStatus.Ambiguous)
                return BatchApplyFailure(RpgGeekMetadataOperationError.AmbiguousRequiresIndividualReview);
            if (volume.RpgGeekMatchStatus != RpgGeekMatchStatus.Candidate)
                return BatchApplyFailure(RpgGeekMetadataOperationError.CandidateNotReady);
            if (request.ProductId <= 0) return BatchApplyFailure(RpgGeekMetadataOperationError.ProductNotFound);
        }

        var products = new Dictionary<int, RpgGeekProduct>();
        var changedPreviews = new List<RpgGeekCandidateBatchPreview>();
        foreach (var request in requests)
        {
            RpgGeekProduct? product;
            try
            {
                product = await client.GetProductAsync(request.ProductId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning("RPGGeek batch confirmation failed for volume {VolumeId} ({ErrorType})",
                    request.VolumeId, exception.GetType().Name);
                return BatchApplyFailure(RpgGeekMetadataOperationError.ProviderRequestFailed);
            }

            if (product is null) return BatchApplyFailure(RpgGeekMetadataOperationError.ProductNotFound);
            products[request.VolumeId] = product;
            var currentPreview = new RpgGeekCandidatePreview(product, Fingerprint(product));
            if (!string.Equals(request.PreviewFingerprint, currentPreview.Fingerprint, StringComparison.Ordinal))
            {
                changedPreviews.Add(new RpgGeekCandidateBatchPreview(request.VolumeId, currentPreview));
            }
        }

        if (changedPreviews.Count > 0)
            return BatchApplyFailure(RpgGeekMetadataOperationError.PreviewChanged, changedPreviews);

        var now = DateTime.UtcNow;
        foreach (var request in requests)
        {
            var volume = volumesById[request.VolumeId];
            var product = products[request.VolumeId];
            ApplyConfirmedFields(volume, product, request);
            volume.RpgGeekId = product.Id;
            volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;
            volume.RpgGeekLastCheckedUtc = now;
            unitOfWork.VolumeRepository.Update(volume);
        }

        if (unitOfWork.HasChanges()) await unitOfWork.CommitAsync(cancellationToken);

        foreach (var request in requests)
        {
            var volume = volumesById[request.VolumeId];
            var product = products[request.VolumeId];
            if ((request.ReplaceCover || string.IsNullOrWhiteSpace(volume.CoverImage)) &&
                !volume.CoverImageLocked && !string.IsNullOrWhiteSpace(product.ImageUrl))
            {
                try
                {
                    await coverDbService.SetVolumeCoverByUrl(volume, product.ImageUrl,
                        fromBase64: false, chooseBetterImage: false, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning("RPGGeek cover could not be applied to volume {VolumeId} ({ErrorType})",
                        volume.Id, exception.GetType().Name);
                }
            }
        }

        await eventHub.SendMessageAsync(MessageFactory.SeriesUpdated,
            MessageFactory.SeriesUpdatedEvent(seriesId), false, cancellationToken);
        return new RpgGeekCandidateBatchApplyResult(true, RpgGeekMetadataOperationError.None,
            Array.Empty<RpgGeekCandidateBatchPreview>());
    }

    private static RpgGeekCandidateBatchApplyResult BatchApplyFailure(
        RpgGeekMetadataOperationError error,
        IReadOnlyList<RpgGeekCandidateBatchPreview>? previews = null) =>
        new(false, error, previews ?? Array.Empty<RpgGeekCandidateBatchPreview>());

    private async Task<Volume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken)
    {
        return await unitOfWork.DataContext.Volume
            .Include(volume => volume.Series)
            .ThenInclude(series => series.Library)
            .FirstOrDefaultAsync(volume => volume.Id == volumeId, cancellationToken);
    }

    private static RpgGeekMetadataOperationError GetEligibilityError(Volume volume)
    {
        if (volume.Series.Library.Type != LibraryType.Rpg) return RpgGeekMetadataOperationError.NotRpgLibrary;
        if (!volume.RpgMaterialType.IsPublication()) return RpgGeekMetadataOperationError.NotPublication;
        return volume.Series.Library.EnableRpgGeekMetadata
            ? RpgGeekMetadataOperationError.None
            : RpgGeekMetadataOperationError.ProviderDisabled;
    }

    private static void ApplyConfirmedFields(
        Volume volume,
        RpgGeekProduct product,
        RpgGeekCandidateApplyRequest request)
    {
        if (!volume.NameLocked && !string.IsNullOrWhiteSpace(product.Title) &&
            (request.ReplaceTitle || string.IsNullOrWhiteSpace(volume.Name)))
        {
            volume.Name = product.Title;
        }

        if (!volume.SummaryLocked && !string.IsNullOrWhiteSpace(product.Description) &&
            (request.ReplaceSummary || string.IsNullOrWhiteSpace(volume.Summary)))
        {
            volume.Summary = product.Description;
        }

        if (!volume.RpgPublicationYearLocked && product.YearPublished.HasValue &&
            (request.ReplaceYear || !volume.RpgPublicationYear.HasValue))
        {
            volume.RpgPublicationYear = product.YearPublished;
        }

        if (!volume.RpgWritersLocked && product.Designers.Count > 0 &&
            (request.ReplaceWriters || volume.RpgWriters.Count == 0))
        {
            volume.RpgWriters = product.Designers.ToList();
        }

        if (!volume.RpgPublishersLocked && product.Publishers.Count > 0 &&
            (request.ReplacePublishers || volume.RpgPublishers.Count == 0))
        {
            volume.RpgPublishers = product.Publishers.ToList();
        }

    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (unitOfWork.HasChanges())
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
    }

    private static void Complete(Volume volume, RpgGeekMatchStatus status)
    {
        volume.RpgGeekMatchStatus = status;
        volume.RpgGeekLastCheckedUtc = DateTime.UtcNow;
    }

    private static string Fingerprint(RpgGeekProduct product)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(product);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    private static RpgGeekCandidateSearchResult SearchFailure(RpgGeekMetadataOperationError error) =>
        new(false, error, RpgGeekMatchStatus.Failed, Array.Empty<RpgGeekSearchResult>());

    private static RpgGeekCandidatePreviewResult PreviewFailure(RpgGeekMetadataOperationError error) =>
        new(false, error, null);

    private static RpgGeekCandidateApplyResult ApplyFailure(RpgGeekMetadataOperationError error) =>
        new(false, error, null);
}
