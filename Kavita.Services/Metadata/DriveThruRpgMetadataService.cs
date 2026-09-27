using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Kavita.API.Database;
using Kavita.API.Repositories;
using Kavita.API.Services;
using Kavita.API.Services.Metadata;
using Kavita.API.Services.SignalR;
using Kavita.Common.Extensions;
using Kavita.Models.DTOs.SignalR;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kavita.Services.Metadata;

public sealed class DriveThruRpgMetadataService(
    IUnitOfWork unitOfWork,
    IDriveThruRpgClient client,
    ICoverDbService coverDbService,
    IEventHub eventHub,
    ILogger<DriveThruRpgMetadataService> logger) : IDriveThruRpgMetadataService
{
    private static readonly Regex TitleSegmentSeparatorRegex = new(@"\s+[-–—]\s+", RegexOptions.Compiled);

    [DisableConcurrentExecution(3600)]
    public async Task MatchUnmatchedChaptersInLibraryAsync(int libraryId, CancellationToken ct = default)
    {
        var library = await unitOfWork.LibraryRepository.GetLibraryForIdAsync(libraryId, ct: ct);
        if (library is not { EnableDriveThruRpgMetadata: true }) return;

        if (library.Type == LibraryType.Rpg)
        {
            var volumeIds = await unitOfWork.DataContext.Volume
                .Where(volume => volume.Series.LibraryId == libraryId && volume.DriveThruRpgId == null
                    && (volume.RpgMaterialType == RpgMaterialType.CoreManual
                        || volume.RpgMaterialType == RpgMaterialType.Manual
                        || volume.RpgMaterialType == RpgMaterialType.Adventure
                        || volume.RpgMaterialType == RpgMaterialType.Supplement
                        || volume.RpgMaterialType == RpgMaterialType.OtherPublication))
                .Select(volume => volume.Id)
                .ToListAsync(ct);
            await MatchUnmatchedVolumesAsync(volumeIds, ct);
            return;
        }

        var chapterIds = await unitOfWork.DataContext.Chapter
            .Where(chapter => chapter.Volume.Series.LibraryId == libraryId && chapter.DriveThruRpgId == null)
            .Select(chapter => chapter.Id)
            .ToListAsync(ct);

        await MatchUnmatchedChaptersAsync(chapterIds, ct);
    }

    [DisableConcurrentExecution(3600)]
    public async Task MatchUnmatchedChaptersAsync(IList<int> chapterIds, CancellationToken ct = default)
    {
        foreach (var chapterId in chapterIds.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var libraryEnabled = await IsLibraryEnabledForChapterAsync(chapterId, ct);
                if (libraryEnabled)
                {
                    await ProcessChapterAsync(chapterId, allowExistingId: false, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DriveThruRPG metadata lookup failed for chapter {ChapterId}", chapterId);
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(350), ct);
                }
            }
        }
    }

    public async Task RefreshChapterAsync(int chapterId, CancellationToken ct = default)
    {
        if (!await IsLibraryEnabledForChapterAsync(chapterId, ct)) return;
        await ProcessChapterAsync(chapterId, allowExistingId: true, ct);
    }

    [DisableConcurrentExecution(3600)]
    public async Task MatchUnmatchedVolumesAsync(IList<int> volumeIds, CancellationToken ct = default)
    {
        foreach (var volumeId in volumeIds.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await IsLibraryEnabledForVolumeAsync(volumeId, ct))
                {
                    await ProcessVolumeAsync(volumeId, allowExistingId: true, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DriveThruRPG metadata lookup failed for Manual {VolumeId}", volumeId);
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(350), ct);
                }
            }
        }
    }

    public async Task RefreshVolumeAsync(int volumeId, CancellationToken ct = default)
    {
        if (!await IsLibraryEnabledForVolumeAsync(volumeId, ct)) return;
        await ProcessVolumeAsync(volumeId, allowExistingId: true, ct, manualOverride: true);
    }

    private async Task ProcessVolumeAsync(int volumeId, bool allowExistingId, CancellationToken ct,
        bool manualOverride = false)
    {
        var volume = await unitOfWork.VolumeRepository.GetVolumeByIdAsync(volumeId,
            VolumeIncludes.Chapters | VolumeIncludes.People | VolumeIncludes.Files, ct);
        if (volume == null) return;

        var legacyIds = volume.Chapters.Select(chapter => chapter.DriveThruRpgId)
            .Where(id => id is > 0).Distinct().ToList();
        var copiedLegacyId = false;
        if (!volume.DriveThruRpgId.HasValue && legacyIds.Count == 1)
        {
            volume.DriveThruRpgId = legacyIds[0];
            copiedLegacyId = true;
        }
        if (!manualOverride && (legacyIds.Count > 1 || (legacyIds.Count == 1 && volume.DriveThruRpgId.HasValue
            && legacyIds[0] != volume.DriveThruRpgId)))
        {
            logger.LogWarning("Manual {VolumeId} has conflicting legacy DriveThruRPG IDs; leaving it unchanged", volumeId);
            return;
        }

        if (!allowExistingId && volume.DriveThruRpgId.HasValue && !copiedLegacyId) return;

        try
        {
            var hasProduct = false;
            var isAmbiguous = false;
            DriveThruRpgProduct? product = null;
            if (volume.DriveThruRpgId is > 0)
            {
                product = await client.GetProductAsync(volume.DriveThruRpgId.Value, ct);
                hasProduct = product != null;
                if (!hasProduct)
                {
                    await SaveMatchOutcomeAsync(volume, DriveThruRpgMatchStatus.Failed, ct);
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(volume.Name))
                {
                    await SaveMatchOutcomeAsync(volume, DriveThruRpgMatchStatus.NoMatch, ct);
                    return;
                }

                var (exactMatch, ambiguous) = await FindUniqueMatchOutcomeAsync(volume.Name, ct);
                if (exactMatch == null)
                {
                    await SaveMatchOutcomeAsync(volume, ambiguous ? DriveThruRpgMatchStatus.Ambiguous : DriveThruRpgMatchStatus.NoMatch, ct);
                    return;
                }

                product = await client.GetProductAsync(exactMatch.ProductId, ct);
                hasProduct = product != null;
                if (!hasProduct)
                {
                    await SaveMatchOutcomeAsync(volume, DriveThruRpgMatchStatus.Failed, ct);
                    return;
                }
            }

            foreach (var chapter in volume.Chapters)
            {
                if (!chapter.WriterLocked && product!.Authors.Count > 0)
                {
                    await PersonHelper.UpdateChapterPeopleAsync(chapter, product.Authors.ToList(), PersonRole.Writer, unitOfWork);
                }

                if (!chapter.PublisherLocked && !string.IsNullOrWhiteSpace(product.Publisher))
                {
                    await PersonHelper.UpdateChapterPeopleAsync(chapter, [product.Publisher], PersonRole.Publisher, unitOfWork);
                }

                ApplySharedMetadataFields(chapter, product);
                chapter.DriveThruRpgId = null;
                unitOfWork.ChapterRepository.Update(chapter);
            }

            if (!volume.NameLocked)
            {
                volume.Name = product!.Title;
            }

            ApplyVolumeMetadataFields(volume, product!);
            volume.DriveThruRpgId = product!.ProductId;
            await SaveMatchOutcomeAsync(volume, DriveThruRpgMatchStatus.Linked, ct);

            if (!volume.CoverImageLocked && !string.IsNullOrWhiteSpace(product.CoverUrl))
            {
                await coverDbService.SetVolumeCoverByUrl(volume, product.CoverUrl, fromBase64: false,
                    chooseBetterImage: true, ct);
            }

            await eventHub.SendMessageAsync(MessageFactory.SeriesUpdated,
                MessageFactory.SeriesUpdatedEvent(volume.SeriesId), false, ct);
        }
        catch (Exception ex)
        {
            await TrySaveMatchOutcomeAsync(volume, DriveThruRpgMatchStatus.Failed, ct);
            logger.LogWarning(ex, "DriveThruRPG lookup failed for Manual {VolumeId}", volumeId);
            throw;
        }
    }

    private async Task SaveMatchOutcomeAsync(Volume volume, DriveThruRpgMatchStatus status, CancellationToken ct)
    {
        volume.DriveThruRpgMatchStatus = status;
        volume.DriveThruRpgLastCheckedUtc = DateTime.UtcNow;
        unitOfWork.VolumeRepository.Update(volume);
        await unitOfWork.CommitAsync(ct);
    }

    private async Task TrySaveMatchOutcomeAsync(Volume volume, DriveThruRpgMatchStatus status, CancellationToken ct)
    {
        try
        {
            await SaveMatchOutcomeAsync(volume, status, ct);
        }
        catch (Exception saveEx)
        {
            logger.LogError(saveEx, "Could not persist DriveThruRPG match outcome for Manual {VolumeId}", volume.Id);
        }
    }

    public async Task<IReadOnlyList<DriveThruRpgSearchResult>> GetCandidatesAsync(int volumeId, string? query = null, CancellationToken ct = default)
    {
        var volume = await unitOfWork.VolumeRepository.GetVolumeByIdAsync(volumeId, ct: ct);
        if (volume == null) return [];
        if (!await IsLibraryEnabledForVolumeAsync(volumeId, ct)) return [];

        var searchTitle = !string.IsNullOrWhiteSpace(query) ? query!.Trim() : volume.Name;
        if (string.IsNullOrWhiteSpace(searchTitle)) return [];

        var seen = new HashSet<int>();
        var candidates = new List<DriveThruRpgSearchResult>();
        foreach (var candidateTitle in BuildSearchTitleCandidates(searchTitle))
        {
            var results = await client.SearchProductsAsync(candidateTitle, ct);
            foreach (var result in results)
            {
                if (seen.Add(result.ProductId)) candidates.Add(result);
            }

            if (candidates.Count > 0) break;
        }

        return candidates.Take(25).ToList();
    }

    public async Task<bool> LinkVolumeAsync(int volumeId, int productId, CancellationToken ct = default)
    {
        if (productId <= 0) return false;
        if (!await IsLibraryEnabledForVolumeAsync(volumeId, ct)) return false;

        var volume = await unitOfWork.VolumeRepository.GetVolumeByIdAsync(volumeId, ct: ct);
        if (volume == null) return false;

        volume.DriveThruRpgId = productId;
        volume.DriveThruRpgMatchStatus = DriveThruRpgMatchStatus.Linked;
        volume.DriveThruRpgLastCheckedUtc = DateTime.UtcNow;
        unitOfWork.VolumeRepository.Update(volume);
        return await unitOfWork.CommitAsync(ct);
    }

    internal static DriveThruRpgMatchStatus ResolveMatchStatus(bool isAmbiguous, bool hasProduct)
    {
        if (hasProduct) return DriveThruRpgMatchStatus.Linked;
        return isAmbiguous ? DriveThruRpgMatchStatus.Ambiguous : DriveThruRpgMatchStatus.NoMatch;
    }

    private async Task ProcessChapterAsync(int chapterId, bool allowExistingId, CancellationToken ct)
    {
        var chapter = await unitOfWork.ChapterRepository.GetChapterAsync(chapterId,
            ChapterIncludes.Volumes | ChapterIncludes.People | ChapterIncludes.Files, ct);
        if (chapter == null) return;
        if (!allowExistingId && chapter.DriveThruRpgId.HasValue) return;

        DriveThruRpgProduct? product;
        if (chapter.DriveThruRpgId is > 0)
        {
            product = await client.GetProductAsync(chapter.DriveThruRpgId.Value, ct);
        }
        else
        {
            var sourceTitle = string.IsNullOrWhiteSpace(chapter.TitleName) ? chapter.Title : chapter.TitleName;
            if (string.IsNullOrWhiteSpace(sourceTitle)) return;

            var exactMatch = await FindUniqueMatchAsync(sourceTitle, ct);

            // Ambiguous titles are intentionally left unmatched.
            if (exactMatch == null) return;
            product = await client.GetProductAsync(exactMatch.ProductId, ct);
        }

        if (product == null) return;

        if (!chapter.WriterLocked && product.Authors.Count > 0)
        {
            await PersonHelper.UpdateChapterPeopleAsync(chapter, product.Authors.ToList(), PersonRole.Writer, unitOfWork);
        }

        if (!chapter.PublisherLocked && !string.IsNullOrWhiteSpace(product.Publisher))
        {
            await PersonHelper.UpdateChapterPeopleAsync(chapter, [product.Publisher], PersonRole.Publisher, unitOfWork);
        }

        ApplyChapterMetadataFields(chapter, product);
        unitOfWork.ChapterRepository.Update(chapter);
        await unitOfWork.CommitAsync(ct);

        if (!chapter.CoverImageLocked && !string.IsNullOrWhiteSpace(product.CoverUrl))
        {
            await coverDbService.SetChapterCoverByUrl(chapter, product.CoverUrl, fromBase64: false,
                chooseBetterImage: true, ct);
        }

        var seriesId = await unitOfWork.ChapterRepository.GetSeriesIdForChapter(chapter.Id, ct);
        if (seriesId.HasValue)
        {
            await eventHub.SendMessageAsync(MessageFactory.ChapterUpdated,
                MessageFactory.ChapterUpdatedEvent(chapter.Id, seriesId.Value), false, ct);
        }
    }

    private Task<bool> IsLibraryEnabledForChapterAsync(int chapterId, CancellationToken ct)
    {
        return unitOfWork.DataContext.Chapter
            .AnyAsync(chapter => chapter.Id == chapterId && chapter.Volume.Series.Library.EnableDriveThruRpgMetadata
                && chapter.Volume.Series.Library.Type != LibraryType.Rpg, ct);
    }

    private Task<bool> IsLibraryEnabledForVolumeAsync(int volumeId, CancellationToken ct)
    {
        return unitOfWork.DataContext.Volume
            .AnyAsync(volume => volume.Id == volumeId && volume.Series.Library.EnableDriveThruRpgMetadata
                && volume.Series.Library.Type == LibraryType.Rpg && (volume.RpgMaterialType == RpgMaterialType.CoreManual
                    || volume.RpgMaterialType == RpgMaterialType.Manual
                    || volume.RpgMaterialType == RpgMaterialType.Adventure
                    || volume.RpgMaterialType == RpgMaterialType.Supplement
                    || volume.RpgMaterialType == RpgMaterialType.OtherPublication), ct);
    }

    internal static void ApplyVolumeMetadataFields(Volume volume, DriveThruRpgProduct product)
    {
        if (!volume.SummaryLocked && !string.IsNullOrWhiteSpace(product.Description))
        {
            volume.Summary = product.Description;
        }

        if (!volume.ReleaseDateLocked && product.ReleaseDate.HasValue)
        {
            volume.ReleaseDate = product.ReleaseDate.Value;
        }

        if (!volume.LanguageLocked && !string.IsNullOrWhiteSpace(product.LanguageCode))
        {
            volume.Language = product.LanguageCode;
        }
    }

    internal static void ApplySharedMetadataFields(Chapter chapter, DriveThruRpgProduct product)
    {
        if (!chapter.SummaryLocked && !string.IsNullOrWhiteSpace(product.Description))
        {
            chapter.Summary = product.Description;
        }

        if (!chapter.ReleaseDateLocked && product.ReleaseDate.HasValue)
        {
            chapter.ReleaseDate = product.ReleaseDate.Value;
        }

        if (!chapter.LanguageLocked && !string.IsNullOrWhiteSpace(product.LanguageCode))
        {
            chapter.Language = product.LanguageCode;
        }
    }

    internal static void ApplyChapterMetadataFields(Chapter chapter, DriveThruRpgProduct product)
    {
        if (!chapter.TitleNameLocked && !string.IsNullOrWhiteSpace(product.Title))
        {
            chapter.TitleName = product.Title;
        }

        if (!chapter.SummaryLocked && !string.IsNullOrWhiteSpace(product.Description))
        {
            chapter.Summary = product.Description;
        }

        if (!chapter.ReleaseDateLocked && product.ReleaseDate.HasValue)
        {
            chapter.ReleaseDate = product.ReleaseDate.Value;
        }

        if (!chapter.LanguageLocked && !string.IsNullOrWhiteSpace(product.LanguageCode))
        {
            chapter.Language = product.LanguageCode;
        }

        chapter.DriveThruRpgId = product.ProductId;
    }

    private async Task<(DriveThruRpgSearchResult? Match, bool IsAmbiguous)> FindUniqueMatchOutcomeAsync(string title, CancellationToken ct)
    {
        foreach (var candidateTitle in BuildSearchTitleCandidates(title))
        {
            var results = await client.SearchProductsAsync(candidateTitle, ct);
            var (match, isAmbiguous) = FindTitleMatch(candidateTitle, results);

            if (isAmbiguous) return (null, true);
            if (match != null) return (match, false);
        }

        return (null, false);
    }

    private async Task<DriveThruRpgSearchResult?> FindUniqueMatchAsync(string title, CancellationToken ct)
    {
        var (match, _) = await FindUniqueMatchOutcomeAsync(title, ct);
        return match;
    }

    internal static DriveThruRpgSearchResult? FindUniqueMatch(string title, IReadOnlyList<DriveThruRpgSearchResult> results)
    {
        var (match, _) = FindTitleMatch(title, results);
        return match;
    }

    internal static (DriveThruRpgSearchResult? Match, bool IsAmbiguous) FindTitleMatch(string title, IReadOnlyList<DriveThruRpgSearchResult> results)
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

    internal static IReadOnlyList<string> BuildSearchTitleCandidates(string title)
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
        return string.Concat(title.ToNormalized().Where(char.IsLetterOrDigit));
    }
}
