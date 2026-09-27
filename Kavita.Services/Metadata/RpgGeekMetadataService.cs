using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed class RpgGeekMetadataService(
    IUnitOfWork unitOfWork,
    IRpgGeekClient client,
    ICoverDbService coverDbService,
    IEventHub eventHub,
    ILogger<RpgGeekMetadataService> logger) : IRpgGeekMetadataService
{
    private static readonly RpgMaterialType[] PublicationTypes =
    [
        RpgMaterialType.CoreManual, RpgMaterialType.Manual, RpgMaterialType.Adventure,
        RpgMaterialType.Supplement, RpgMaterialType.OtherPublication
    ];

    /// <summary>
    /// Backfill every unlinked publication in the library. Publications already linked to
    /// DriveThruRPG are skipped: RPGGeek is meant to cover the gaps, not to duplicate calls.
    /// </summary>
    [DisableConcurrentExecution(3600)]
    public async Task MatchUnmatchedVolumesInLibraryAsync(int libraryId, CancellationToken ct = default)
    {
        if (!client.IsEnabled)
        {
            logger.LogWarning("RPGGeek metadata is enabled for library {LibraryId} but no BGG_API_TOKEN is configured", libraryId);
            return;
        }

        var library = await unitOfWork.LibraryRepository.GetLibraryForIdAsync(libraryId, ct: ct);
        if (library is not { EnableRpgGeekMetadata: true, Type: LibraryType.Rpg }) return;

        var volumeIds = await unitOfWork.DataContext.Volume
            .Where(volume => volume.Series.LibraryId == libraryId
                && volume.RpgGeekId == null
                && volume.DriveThruRpgId == null
                && PublicationTypes.Contains(volume.RpgMaterialType))
            .Select(volume => volume.Id)
            .ToListAsync(ct);

        logger.LogInformation("RPGGeek backfill for library {LibraryId}: {VolumeCount} candidate publications",
            libraryId, volumeIds.Count);

        await MatchUnmatchedVolumesAsync(volumeIds, ct);
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
                    await ProcessVolumeAsync(volumeId, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RPGGeek metadata lookup failed for publication {VolumeId}", volumeId);
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                }
            }
        }
    }

    public async Task RefreshVolumeAsync(int volumeId, CancellationToken ct = default)
    {
        if (!await IsLibraryEnabledForVolumeAsync(volumeId, ct)) return;
        await ProcessVolumeAsync(volumeId, ct);
    }

    public async Task<IReadOnlyList<RpgGeekSearchResult>> GetCandidatesAsync(int volumeId, string? query = null, CancellationToken ct = default)
    {
        if (!client.IsEnabled) return [];

        var volume = await unitOfWork.VolumeRepository.GetVolumeByIdAsync(volumeId, ct: ct);
        if (volume == null) return [];
        if (!await IsLibraryEnabledForVolumeAsync(volumeId, ct)) return [];

        var searchTitle = !string.IsNullOrWhiteSpace(query) ? query!.Trim() : volume.Name;
        if (string.IsNullOrWhiteSpace(searchTitle)) return [];

        return (await client.SearchProductsAsync(searchTitle, ct)).Take(25).ToList();
    }

    public async Task<bool> LinkVolumeAsync(int volumeId, int rpgGeekId, CancellationToken ct = default)
    {
        if (rpgGeekId <= 0) return false;
        if (!await IsLibraryEnabledForVolumeAsync(volumeId, ct)) return false;

        var volume = await unitOfWork.VolumeRepository.GetVolumeByIdAsync(volumeId, ct: ct);
        if (volume == null) return false;

        volume.RpgGeekId = rpgGeekId;
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;
        volume.RpgGeekLastCheckedUtc = DateTime.UtcNow;
        unitOfWork.VolumeRepository.Update(volume);
        return await unitOfWork.CommitAsync(ct);
    }

    private async Task ProcessVolumeAsync(int volumeId, CancellationToken ct)
    {
        var volume = await unitOfWork.VolumeRepository.GetVolumeByIdAsync(volumeId,
            VolumeIncludes.Chapters | VolumeIncludes.People | VolumeIncludes.Files, ct);
        if (volume == null) return;

        try
        {
            RpgGeekProduct? product;
            if (volume.RpgGeekId is > 0)
            {
                product = await client.GetProductAsync(volume.RpgGeekId.Value, ct);
                if (product == null)
                {
                    await SaveMatchOutcomeAsync(volume, RpgGeekMatchStatus.Failed, ct);
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(volume.Name))
                {
                    await SaveMatchOutcomeAsync(volume, RpgGeekMatchStatus.NoMatch, ct);
                    return;
                }

                var (match, ambiguous, searchedTitle) = await FindBestMatchAsync(volume, ct);
                if (match == null)
                {
                    await SaveMatchOutcomeAsync(volume, ambiguous ? RpgGeekMatchStatus.Ambiguous : RpgGeekMatchStatus.NoMatch, ct);
                    return;
                }

                product = await client.GetProductAsync(match.Id, ct);
                if (product == null)
                {
                    await SaveMatchOutcomeAsync(volume, RpgGeekMatchStatus.Failed, ct);
                    return;
                }
            }

            ApplyRpgGeekScalarMetadata(volume, product!);
            await ApplyRpgGeekPeopleAsync(volume, product!, ct);
            volume.RpgGeekId = product!.Id;
            await SaveMatchOutcomeAsync(volume, RpgGeekMatchStatus.Linked, ct);

            if (volume.CoverImageLocked == false && string.IsNullOrWhiteSpace(volume.CoverImage)
                && !string.IsNullOrWhiteSpace(product.ImageUrl))
            {
                await coverDbService.SetVolumeCoverByUrl(volume, product.ImageUrl, fromBase64: false,
                    chooseBetterImage: false, ct);
            }

            await eventHub.SendMessageAsync(MessageFactory.SeriesUpdated,
                MessageFactory.SeriesUpdatedEvent(volume.SeriesId), false, ct);
        }
        catch (Exception ex)
        {
            await TrySaveMatchOutcomeAsync(volume, RpgGeekMatchStatus.Failed, ct);
            logger.LogWarning(ex, "RPGGeek lookup failed for publication {VolumeId}", volumeId);
            throw;
        }
    }

    /// <summary>
    /// Fills only empty, unlocked fields. RPGGeek never overwrites manual edits or data that
    /// DriveThruRPG already provided; title and language are intentionally left untouched.
    /// </summary>
    internal static void ApplyRpgGeekScalarMetadata(Volume volume, RpgGeekProduct product)
    {
        if (volume.SummaryLocked == false && string.IsNullOrWhiteSpace(volume.Summary)
            && !string.IsNullOrWhiteSpace(product.Description))
        {
            volume.Summary = product.Description;
        }

        if (volume.ReleaseDateLocked == false && volume.ReleaseDate == null && product.YearPublished is > 0)
        {
            volume.ReleaseDate = new DateTime(product.YearPublished.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
    }

    private async Task ApplyRpgGeekPeopleAsync(Volume volume, RpgGeekProduct product, CancellationToken ct)
    {
        foreach (var chapter in volume.Chapters)
        {
            if (chapter.WriterLocked == false && chapter.People.All(person => person.Role != PersonRole.Writer)
                && product.Designers.Count > 0)
            {
                await PersonHelper.UpdateChapterPeopleAsync(chapter, product.Designers.ToList(), PersonRole.Writer, unitOfWork);
            }

            if (chapter.PublisherLocked == false && chapter.People.All(person => person.Role != PersonRole.Publisher)
                && product.Publishers.Count > 0)
            {
                await PersonHelper.UpdateChapterPeopleAsync(chapter, product.Publishers.ToList(), PersonRole.Publisher, unitOfWork);
            }
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Searches by the publication title and, when that title is generic ("Core Book",
    /// "Remastered", …), falls back to the game (series) name. Each query is cached by
    /// the client, so repeated backfills do not repeat API calls.
    /// </summary>
    private async Task<(RpgGeekSearchResult? Match, bool IsAmbiguous, string SearchedTitle)> FindBestMatchAsync(Volume volume, CancellationToken ct)
    {
        foreach (var title in await BuildSearchTitlesAsync(volume))
        {
            var results = await client.SearchProductsAsync(title, ct);
            var (match, isAmbiguous) = FindTitleMatch(title, results);
            if (isAmbiguous) return (null, true, title);
            if (match != null) return (match, false, title);
        }

        return (null, false, volume.Name);
    }

    private async Task<IReadOnlyList<string>> BuildSearchTitlesAsync(Volume volume)
    {
        var titles = new List<string> { volume.Name };

        var seriesName = await unitOfWork.DataContext.Series
            .Where(series => series.Id == volume.SeriesId)
            .Select(series => series.Name)
            .SingleOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(seriesName) && !string.Equals(seriesName.Trim(), volume.Name, StringComparison.OrdinalIgnoreCase))
        {
            titles.Add(seriesName.Trim());
        }

        return titles;
    }

    internal static (RpgGeekSearchResult? Match, bool IsAmbiguous) FindTitleMatch(string title, IReadOnlyList<RpgGeekSearchResult> results)
    {
        var normalizedTitle = NormalizeTitle(title);
        var exactMatches = results
            .Where(result => NormalizeTitle(result.Name) == normalizedTitle)
            .Take(2)
            .ToList();

        if (exactMatches.Count == 1) return (exactMatches[0], false);
        if (exactMatches.Count > 1) return (null, true);

        // Fall back to a single unique prefix match ("Heart" → "Heart: The City Beneath").
        var prefixMatches = results
            .Where(result => NormalizeTitle(result.Name).StartsWith(normalizedTitle, StringComparison.Ordinal))
            .Take(2)
            .ToList();

        return prefixMatches.Count switch
        {
            1 => (prefixMatches[0], false),
            > 1 => (null, true),
            _ => (null, false)
        };
    }

    private static string NormalizeTitle(string title)
    {
        return string.Concat(title.ToNormalized().Where(char.IsLetterOrDigit));
    }

    private async Task SaveMatchOutcomeAsync(Volume volume, RpgGeekMatchStatus status, CancellationToken ct)
    {
        volume.RpgGeekMatchStatus = status;
        volume.RpgGeekLastCheckedUtc = DateTime.UtcNow;
        unitOfWork.VolumeRepository.Update(volume);
        await unitOfWork.CommitAsync(ct);
    }

    private async Task TrySaveMatchOutcomeAsync(Volume volume, RpgGeekMatchStatus status, CancellationToken ct)
    {
        try
        {
            await SaveMatchOutcomeAsync(volume, status, ct);
        }
        catch (Exception saveEx)
        {
            logger.LogError(saveEx, "Could not persist RPGGeek match outcome for publication {VolumeId}", volume.Id);
        }
    }

    private Task<bool> IsLibraryEnabledForVolumeAsync(int volumeId, CancellationToken ct)
    {
        return unitOfWork.DataContext.Volume
            .AnyAsync(volume => volume.Id == volumeId && volume.Series.Library.EnableRpgGeekMetadata
                && volume.Series.Library.Type == LibraryType.Rpg && PublicationTypes.Contains(volume.RpgMaterialType), ct);
    }
}
