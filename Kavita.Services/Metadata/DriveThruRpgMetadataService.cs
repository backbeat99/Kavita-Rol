using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Kavita.API.Database;
using Kavita.API.Repositories;
using Kavita.API.Services.Metadata;
using Kavita.API.Services.SignalR;
using Kavita.Models.DTOs.SignalR;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kavita.Services.Metadata;

/// <summary>
/// Matches only explicitly classified RPG publications. Automatic association requires one normalized exact title;
/// ambiguous and fuzzy results remain unlinked for review.
/// </summary>
public sealed class DriveThruRpgMetadataService(
    IUnitOfWork unitOfWork,
    IDriveThruRpgClient client,
    ICoverDbService coverDbService,
    IEventHub eventHub,
    ILogger<DriveThruRpgMetadataService> logger) : IDriveThruRpgMetadataService
{
    private static readonly Regex TitleSegmentSeparatorRegex = new(@"\s+[-–—]\s+", RegexOptions.Compiled);
    private static readonly RpgMaterialType[] PublicationTypes =
    [
        RpgMaterialType.CoreManual,
        RpgMaterialType.Manual,
        RpgMaterialType.Adventure,
        RpgMaterialType.Supplement,
        RpgMaterialType.OtherPublication,
    ];

    [DisableConcurrentExecution(3600)]
    public async Task MatchUnmatchedVolumesInLibraryAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        var library = await unitOfWork.LibraryRepository.GetLibraryForIdAsync(libraryId, ct: cancellationToken);
        if (library is not { Type: LibraryType.Rpg, EnableDriveThruRpgMetadata: true }) return;

        var volumeIds = await unitOfWork.DataContext.Volume
            .Where(volume => volume.Series.LibraryId == libraryId && volume.DriveThruRpgId == null &&
                             PublicationTypes.Contains(volume.RpgMaterialType))
            .Select(volume => volume.Id)
            .ToListAsync(cancellationToken);

        await MatchUnmatchedVolumesAsync(volumeIds, cancellationToken);
    }

    [DisableConcurrentExecution(3600)]
    public async Task MatchUnmatchedVolumesAsync(IList<int> volumeIds, CancellationToken cancellationToken = default)
    {
        foreach (var volumeId in volumeIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessVolumeAsync(volumeId, allowExistingId: false, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning("DriveThruRPG lookup failed for publication {VolumeId} ({ErrorType})",
                    volumeId, exception.GetType().Name);
                await TryCompleteWithFailureAsync(volumeId, cancellationToken);
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
            }
        }
    }

    public async Task<DriveThruRpgCandidateSearchResult> SearchCandidatesForVolumeAsync(
        int volumeId, string? query = null, CancellationToken cancellationToken = default)
    {
        var volume = await GetVolumeAsync(volumeId, cancellationToken);
        if (volume is null) return SearchFailure(DriveThruRpgMetadataOperationError.VolumeNotFound);
        var eligibilityError = GetEligibilityError(volume);
        if (eligibilityError != DriveThruRpgMetadataOperationError.None) return SearchFailure(eligibilityError);

        if (volume.DriveThruRpgId.HasValue)
        {
            return new DriveThruRpgCandidateSearchResult(true, DriveThruRpgMetadataOperationError.None,
                DriveThruRpgMatchStatus.Linked, Array.Empty<DriveThruRpgSearchResult>());
        }

        var searchTerm = string.IsNullOrWhiteSpace(query) ? volume.Name : query.Trim();
        if (string.IsNullOrWhiteSpace(searchTerm)) return SearchFailure(DriveThruRpgMetadataOperationError.QueryRequired);

        try
        {
            var candidates = await client.SearchProductsAsync(searchTerm, cancellationToken);
            var (_, ambiguous) = FindTitleMatch(searchTerm, candidates);
            var status = candidates.Count == 0
                ? DriveThruRpgMatchStatus.NoMatch
                : ambiguous ? DriveThruRpgMatchStatus.Ambiguous : DriveThruRpgMatchStatus.Candidate;
            Complete(volume, status);
            await SaveAsync(cancellationToken);
            return new DriveThruRpgCandidateSearchResult(true, DriveThruRpgMetadataOperationError.None,
                status, candidates.Take(25).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Complete(volume, DriveThruRpgMatchStatus.Failed);
            await SaveAsync(cancellationToken);
            logger.LogWarning("DriveThruRPG candidate search failed for publication {VolumeId} ({ErrorType})",
                volumeId, exception.GetType().Name);
            return SearchFailure(DriveThruRpgMetadataOperationError.ProviderRequestFailed);
        }
    }

    public async Task<DriveThruRpgMetadataOperationResult> LinkVolumeAsync(
        int volumeId, int productId, CancellationToken cancellationToken = default)
    {
        if (productId <= 0) return OperationFailure(DriveThruRpgMetadataOperationError.InvalidProductId);

        var volume = await GetVolumeAsync(volumeId, cancellationToken);
        if (volume is null) return OperationFailure(DriveThruRpgMetadataOperationError.VolumeNotFound);
        var eligibilityError = GetEligibilityError(volume);
        if (eligibilityError != DriveThruRpgMetadataOperationError.None) return OperationFailure(eligibilityError);

        volume.DriveThruRpgId = productId;
        volume.DriveThruRpgMatchStatus = DriveThruRpgMatchStatus.Pending;
        volume.DriveThruRpgLastCheckedUtc = null;
        unitOfWork.VolumeRepository.Update(volume);
        await SaveAsync(cancellationToken);
        return new DriveThruRpgMetadataOperationResult(true, DriveThruRpgMetadataOperationError.None);
    }

    public async Task<DriveThruRpgMetadataOperationResult> RefreshVolumeAsync(
        int volumeId, CancellationToken cancellationToken = default)
    {
        var volume = await GetVolumeAsync(volumeId, cancellationToken);
        if (volume is null) return OperationFailure(DriveThruRpgMetadataOperationError.VolumeNotFound);
        var eligibilityError = GetEligibilityError(volume);
        if (eligibilityError != DriveThruRpgMetadataOperationError.None) return OperationFailure(eligibilityError);

        try
        {
            var succeeded = await ProcessVolumeAsync(volumeId, allowExistingId: true, cancellationToken);
            return succeeded
                ? new DriveThruRpgMetadataOperationResult(true, DriveThruRpgMetadataOperationError.None)
                : OperationFailure(DriveThruRpgMetadataOperationError.ProviderRequestFailed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("DriveThruRPG refresh failed for publication {VolumeId} ({ErrorType})",
                volumeId, exception.GetType().Name);
            await TryCompleteWithFailureAsync(volumeId, cancellationToken);
            return OperationFailure(DriveThruRpgMetadataOperationError.ProviderRequestFailed);
        }
    }

    private async Task<bool> ProcessVolumeAsync(int volumeId, bool allowExistingId, CancellationToken cancellationToken)
    {
        var volume = await GetVolumeAsync(volumeId, cancellationToken);
        if (volume is null || GetEligibilityError(volume) != DriveThruRpgMetadataOperationError.None) return false;
        if (!allowExistingId && volume.DriveThruRpgId.HasValue) return false;

        if (!volume.DriveThruRpgId.HasValue)
        {
            Complete(volume, DriveThruRpgMatchStatus.Pending, checkedAt: null);
            unitOfWork.VolumeRepository.Update(volume);
            await SaveAsync(cancellationToken);
        }

        DriveThruRpgProduct? product;
        if (volume.DriveThruRpgId is > 0)
        {
            product = await client.GetProductAsync(volume.DriveThruRpgId.Value, cancellationToken);
            if (product is null)
            {
                await CompleteAndSaveAsync(volume, DriveThruRpgMatchStatus.Failed, cancellationToken);
                return false;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(volume.Name))
            {
                await CompleteAndSaveAsync(volume, DriveThruRpgMatchStatus.NoMatch, cancellationToken);
                return false;
            }

            var (match, ambiguous) = await FindUniqueMatchOutcomeAsync(volume.Name, cancellationToken);
            if (match is null)
            {
                await CompleteAndSaveAsync(volume,
                    ambiguous ? DriveThruRpgMatchStatus.Ambiguous : DriveThruRpgMatchStatus.NoMatch,
                    cancellationToken);
                return false;
            }

            product = await client.GetProductAsync(match.ProductId, cancellationToken);
            if (product is null)
            {
                await CompleteAndSaveAsync(volume, DriveThruRpgMatchStatus.Failed, cancellationToken);
                return false;
            }
        }

        ApplyProduct(volume, product);
        volume.DriveThruRpgId = product.ProductId;
        Complete(volume, DriveThruRpgMatchStatus.Linked);
        unitOfWork.VolumeRepository.Update(volume);
        await SaveAsync(cancellationToken);

        if (!volume.CoverImageLocked && !string.IsNullOrWhiteSpace(product.CoverUrl))
        {
            try
            {
                await coverDbService.SetVolumeCoverByUrl(volume, product.CoverUrl, fromBase64: false,
                    chooseBetterImage: true, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning("DriveThruRPG cover could not be applied to publication {VolumeId} ({ErrorType})",
                    volume.Id, exception.GetType().Name);
            }
        }

        try
        {
            await eventHub.SendMessageAsync(MessageFactory.SeriesUpdated,
                MessageFactory.SeriesUpdatedEvent(volume.SeriesId), false, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning("DriveThruRPG update event could not be sent for publication {VolumeId} ({ErrorType})",
                volume.Id, exception.GetType().Name);
        }

        return true;
    }

    private async Task<(DriveThruRpgSearchResult? Match, bool IsAmbiguous)> FindUniqueMatchOutcomeAsync(
        string title, CancellationToken cancellationToken)
    {
        foreach (var candidateTitle in BuildSearchTitleCandidates(title))
        {
            var results = await client.SearchProductsAsync(candidateTitle, cancellationToken);
            var (match, isAmbiguous) = FindTitleMatch(candidateTitle, results);
            if (isAmbiguous) return (null, true);
            if (match is not null) return (match, false);
        }

        return (null, false);
    }

    private static (DriveThruRpgSearchResult? Match, bool IsAmbiguous) FindTitleMatch(
        string title, IReadOnlyList<DriveThruRpgSearchResult> results)
    {
        var normalizedTitle = NormalizeTitle(title);
        var exactMatches = results
            .Where(result => !result.Title.Contains("Fantasy Grounds", StringComparison.OrdinalIgnoreCase))
            .Where(result => NormalizeTitle(result.Title) == normalizedTitle)
            .Take(2)
            .ToList();

        return exactMatches.Count switch
        {
            0 => (null, false),
            1 => (exactMatches[0], false),
            _ => (null, true)
        };
    }

    private static IReadOnlyList<string> BuildSearchTitleCandidates(string title)
    {
        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle)) return [];

        var candidates = new List<string>();
        var normalizedCandidates = new HashSet<string>(StringComparer.Ordinal);
        AddSearchTitleCandidate(trimmedTitle, candidates, normalizedCandidates);

        var segments = TitleSegmentSeparatorRegex.Split(trimmedTitle)
            .Select(segment => segment.Trim())
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToList();

        for (var segmentCount = segments.Count - 1; segmentCount >= 1; segmentCount--)
        {
            var trailingSegments = segments.Skip(segmentCount).ToList();
            if (!trailingSegments.All(IsGenericTrailingTitleSegment)) continue;
            AddSearchTitleCandidate(string.Join(" - ", segments.Take(segmentCount)), candidates, normalizedCandidates);
        }

        return candidates;
    }

    private static bool IsGenericTrailingTitleSegment(string segment)
    {
        var normalizedSegment = NormalizeTitle(segment);
        return normalizedSegment is "page" or "pages" or "pdf" or "ebook"
               || normalizedSegment.Contains("core", StringComparison.Ordinal)
               || normalizedSegment.Contains("rulebook", StringComparison.Ordinal)
               || normalizedSegment.Contains("rules", StringComparison.Ordinal)
               || normalizedSegment.Contains("roleplaying", StringComparison.Ordinal)
               || normalizedSegment.EndsWith("rpg", StringComparison.Ordinal);
    }

    private static void AddSearchTitleCandidate(string title, ICollection<string> candidates, ISet<string> normalizedCandidates)
    {
        var normalizedTitle = NormalizeTitle(title);
        if (normalizedTitle.Length < 4 || !normalizedCandidates.Add(normalizedTitle)) return;
        candidates.Add(title.Trim());
    }

    private static string NormalizeTitle(string title)
    {
        var builder = new StringBuilder(title.Length);
        foreach (var character in title.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) !=
                System.Globalization.UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void ApplyProduct(Volume volume, DriveThruRpgProduct product)
    {
        if (!volume.NameLocked && !string.IsNullOrWhiteSpace(product.Title)) volume.Name = product.Title;
        if (!volume.SummaryLocked && !string.IsNullOrWhiteSpace(product.Description)) volume.Summary = product.Description;
        if (!volume.RpgPublicationYearLocked && product.ReleaseDate.HasValue)
            volume.RpgPublicationYear = product.ReleaseDate.Value.Year;
        if (!volume.RpgWritersLocked && product.Authors.Count > 0)
            volume.RpgWriters = product.Authors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!volume.RpgPublishersLocked && !string.IsNullOrWhiteSpace(product.Publisher))
            volume.RpgPublishers = [product.Publisher.Trim()];

        if (!string.IsNullOrWhiteSpace(product.LanguageCode))
        {
            foreach (var chapter in volume.Chapters.Where(chapter => !chapter.LanguageLocked))
            {
                chapter.Language = product.LanguageCode;
            }
        }
    }

    private async Task<Volume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken)
    {
        return await unitOfWork.DataContext.Volume
            .Include(volume => volume.Series)
            .ThenInclude(series => series.Library)
            .Include(volume => volume.Chapters)
            .FirstOrDefaultAsync(volume => volume.Id == volumeId, cancellationToken);
    }

    private static DriveThruRpgMetadataOperationError GetEligibilityError(Volume volume)
    {
        if (volume.Series.Library.Type != LibraryType.Rpg) return DriveThruRpgMetadataOperationError.NotRpgLibrary;
        if (!volume.RpgMaterialType.IsPublication()) return DriveThruRpgMetadataOperationError.NotPublication;
        return volume.Series.Library.EnableDriveThruRpgMetadata
            ? DriveThruRpgMetadataOperationError.None
            : DriveThruRpgMetadataOperationError.ProviderDisabled;
    }

    private static void Complete(Volume volume, DriveThruRpgMatchStatus status, DateTime? checkedAt = null)
    {
        volume.DriveThruRpgMatchStatus = status;
        volume.DriveThruRpgLastCheckedUtc = checkedAt ?? (status == DriveThruRpgMatchStatus.Pending ? null : DateTime.UtcNow);
    }

    private async Task CompleteAndSaveAsync(Volume volume, DriveThruRpgMatchStatus status, CancellationToken cancellationToken)
    {
        Complete(volume, status);
        unitOfWork.VolumeRepository.Update(volume);
        await SaveAsync(cancellationToken);
    }

    private async Task TryCompleteWithFailureAsync(int volumeId, CancellationToken cancellationToken)
    {
        try
        {
            var volume = await GetVolumeAsync(volumeId, cancellationToken);
            if (volume is null || GetEligibilityError(volume) != DriveThruRpgMetadataOperationError.None) return;
            await CompleteAndSaveAsync(volume, DriveThruRpgMatchStatus.Failed, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception saveException)
        {
            logger.LogError("Could not persist DriveThruRPG failure status for publication {VolumeId} ({ErrorType})",
                volumeId, saveException.GetType().Name);
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (unitOfWork.HasChanges()) await unitOfWork.CommitAsync(cancellationToken);
    }

    private static DriveThruRpgCandidateSearchResult SearchFailure(DriveThruRpgMetadataOperationError error) =>
        new(false, error, DriveThruRpgMatchStatus.Failed, Array.Empty<DriveThruRpgSearchResult>());

    private static DriveThruRpgMetadataOperationResult OperationFailure(DriveThruRpgMetadataOperationError error) =>
        new(false, error);
}
