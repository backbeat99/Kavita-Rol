using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kavita.API.Database;
using Kavita.API.Services.Metadata;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Builders;
using Microsoft.EntityFrameworkCore;

namespace Kavita.Services.Metadata;

public sealed class RpgPublicationGroupingService(IUnitOfWork unitOfWork) : IRpgPublicationGroupingService
{
    public async Task<RpgPublicationGroupingResult> GroupVersionsAsync(
        int seriesId,
        int primaryVolumeId,
        IReadOnlyCollection<int> volumeIds,
        CancellationToken cancellationToken = default)
    {
        if (volumeIds.Count < 2 || !volumeIds.Contains(primaryVolumeId))
        {
            return Failure(RpgPublicationGroupingError.InvalidSelection, primaryVolumeId);
        }

        if (volumeIds.Distinct().Count() != volumeIds.Count)
        {
            return Failure(RpgPublicationGroupingError.DuplicateVolumeIds, primaryVolumeId);
        }

        var volumes = await unitOfWork.DataContext.Volume
            .Where(volume => volumeIds.Contains(volume.Id))
            .Include(volume => volume.Chapters)
            .Include(volume => volume.Series)
            .ThenInclude(series => series.Library)
            .ToListAsync(cancellationToken);

        if (volumes.Count != volumeIds.Count)
        {
            return Failure(RpgPublicationGroupingError.VolumeNotFound, primaryVolumeId);
        }

        if (volumes.Any(volume => volume.SeriesId != seriesId))
        {
            return Failure(RpgPublicationGroupingError.WrongSeries, primaryVolumeId);
        }

        if (volumes.Any(volume => volume.Series.Library.Type != LibraryType.Rpg))
        {
            return Failure(RpgPublicationGroupingError.NotRpgLibrary, primaryVolumeId);
        }

        if (volumes.Any(volume => volume.RpgMaterialType.IsResource()))
        {
            return Failure(RpgPublicationGroupingError.ResourceSelected, primaryVolumeId);
        }

        var materialTypes = volumes
            .Select(volume => volume.RpgMaterialType)
            .Where(type => type != RpgMaterialType.Unclassified)
            .Distinct()
            .ToArray();
        if (materialTypes.Length > 1)
        {
            return Failure(RpgPublicationGroupingError.ConflictingMaterialTypes, primaryVolumeId);
        }

        if (volumes.Any(volume => volume.Chapters.Count == 0))
        {
            return Failure(RpgPublicationGroupingError.EmptyPublication, primaryVolumeId);
        }

        var primary = volumes.Single(volume => volume.Id == primaryVolumeId);
        var sources = volumeIds
            .Where(id => id != primaryVolumeId)
            .Select(id => volumes.Single(volume => volume.Id == id))
            .ToArray();

        if (HasConflictingIds(volumes.Select(volume => volume.RpgGeekId)) ||
            HasConflictingIds(volumes.Select(volume => volume.DriveThruRpgId)))
        {
            return Failure(RpgPublicationGroupingError.ConflictingProviderIds, primaryVolumeId);
        }

        if ((!primary.RpgGeekId.HasValue && sources.Any(volume => volume.RpgGeekId.HasValue)) ||
            (!primary.DriveThruRpgId.HasValue && sources.Any(volume => volume.DriveThruRpgId.HasValue)))
        {
            return Failure(RpgPublicationGroupingError.ProviderIdentityNotPrimary, primaryVolumeId);
        }

        MergeSharedBibliography(primary, sources, materialTypes);
        primary.RpgVersionGroupLocked = true;

        foreach (var source in sources)
        {
            foreach (var chapter in source.Chapters.ToArray())
            {
                source.Chapters.Remove(chapter);
                primary.Chapters.Add(chapter);
                chapter.Volume = primary;
                chapter.VolumeId = primary.Id;
            }
        }

        await MoveVolumeReferencesAsync(sources.Select(source => source.Id).ToArray(), primary.Id, cancellationToken);
        primary.Pages = primary.Chapters.Select(chapter => chapter.Pages).DefaultIfEmpty().Max();
        primary.WordCount = primary.Chapters.Sum(chapter => chapter.WordCount);
        primary.LastModified = DateTime.Now;
        primary.LastModifiedUtc = DateTime.UtcNow;

        unitOfWork.VolumeRepository.Remove(sources);
        if (!await unitOfWork.CommitAsync(cancellationToken))
        {
            return Failure(RpgPublicationGroupingError.SaveFailed, primaryVolumeId);
        }

        return new RpgPublicationGroupingResult(true, RpgPublicationGroupingError.None, primary.Id,
            sources.Select(source => source.Id).ToArray());
    }

    public async Task<RpgPublicationVersionSplitResult> SplitVersionAsync(
        int seriesId,
        int volumeId,
        int chapterId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var source = await unitOfWork.DataContext.Volume
            .Where(volume => volume.Id == volumeId)
            .Include(volume => volume.Chapters)
            .Include(volume => volume.Series)
            .ThenInclude(series => series.Library)
            .SingleOrDefaultAsync(cancellationToken);

        if (source is null) return SplitFailure(RpgPublicationVersionSplitError.VolumeNotFound);
        if (source.SeriesId != seriesId) return SplitFailure(RpgPublicationVersionSplitError.WrongSeries);
        if (source.Series.Library.Type != LibraryType.Rpg) return SplitFailure(RpgPublicationVersionSplitError.NotRpgLibrary);
        if (!source.RpgMaterialType.IsPublication()) return SplitFailure(RpgPublicationVersionSplitError.NotPublication);
        if (source.Chapters.Count <= 1) return SplitFailure(RpgPublicationVersionSplitError.LastVersion);
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            return SplitFailure(RpgPublicationVersionSplitError.InvalidTitle);

        var chapter = source.Chapters.SingleOrDefault(item => item.Id == chapterId);
        if (chapter is null) return SplitFailure(RpgPublicationVersionSplitError.VersionNotFound);

        var newTitle = title.Trim();
        var splitVolume = new VolumeBuilder(newTitle)
            .WithSeriesId(seriesId)
            .WithMinNumber(0)
            .WithMaxNumber(0)
            .Build();
        splitVolume.LookupName = newTitle;
        splitVolume.RpgMaterialType = source.RpgMaterialType;
        splitVolume.RpgVersionGroupLocked = true;
        splitVolume.NameLocked = true;
        CopySharedBibliography(source, splitVolume);

        var context = unitOfWork.DataContext;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            unitOfWork.VolumeRepository.Add(splitVolume);
            await context.SaveChangesAsync(cancellationToken);

            source.Chapters.Remove(chapter);
            splitVolume.Chapters.Add(chapter);
            chapter.Volume = splitVolume;
            chapter.VolumeId = splitVolume.Id;
            await MoveChapterReferencesAsync(chapterId, splitVolume.Id, cancellationToken);

            source.Pages = source.Chapters.Select(item => item.Pages).DefaultIfEmpty().Max();
            source.WordCount = source.Chapters.Sum(item => item.WordCount);
            splitVolume.Pages = chapter.Pages;
            splitVolume.WordCount = chapter.WordCount;
            source.LastModified = DateTime.Now;
            source.LastModifiedUtc = DateTime.UtcNow;
            splitVolume.LastModified = source.LastModified;
            splitVolume.LastModifiedUtc = source.LastModifiedUtc;

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RpgPublicationVersionSplitResult(true, RpgPublicationVersionSplitError.None, splitVolume.Id);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task MoveChapterReferencesAsync(int chapterId, int targetVolumeId, CancellationToken cancellationToken)
    {
        var context = unitOfWork.DataContext;

        var progresses = await context.AppUserProgresses
            .Where(item => item.ChapterId == chapterId).ToListAsync(cancellationToken);
        foreach (var item in progresses) item.VolumeId = targetVolumeId;

        var bookmarks = await context.AppUserBookmark
            .Where(item => item.ChapterId == chapterId).ToListAsync(cancellationToken);
        foreach (var item in bookmarks) item.VolumeId = targetVolumeId;

        var annotations = await context.AppUserAnnotation
            .Where(item => item.ChapterId == chapterId).ToListAsync(cancellationToken);
        foreach (var item in annotations) item.VolumeId = targetVolumeId;

        var tableOfContents = await context.AppUserTableOfContent
            .Where(item => item.ChapterId == chapterId).ToListAsync(cancellationToken);
        foreach (var item in tableOfContents) item.VolumeId = targetVolumeId;

        var readingActivities = await context.AppUserReadingSessionActivityData
            .Where(item => item.ChapterId == chapterId).ToListAsync(cancellationToken);
        foreach (var item in readingActivities) item.VolumeId = targetVolumeId;

        var readingListItems = await context.ReadingListItem
            .Where(item => item.ChapterId == chapterId).ToListAsync(cancellationToken);
        foreach (var item in readingListItems) item.VolumeId = targetVolumeId;

        var remapRules = await context.ReadingListRemapRule
            .Where(item => item.ChapterId == chapterId).ToListAsync(cancellationToken);
        foreach (var item in remapRules) item.VolumeId = targetVolumeId;
    }

    private static void CopySharedBibliography(Volume source, Volume target)
    {
        target.CoverImage = source.CoverImage;
        target.CoverImageLocked = source.CoverImageLocked;
        target.Summary = source.Summary;
        target.SummaryLocked = source.SummaryLocked;
        target.RpgPublicationYear = source.RpgPublicationYear;
        target.RpgPublicationYearLocked = source.RpgPublicationYearLocked;
        target.RpgWriters = source.RpgWriters.ToList();
        target.RpgWritersLocked = source.RpgWritersLocked;
        target.RpgPublishers = source.RpgPublishers.ToList();
        target.RpgPublishersLocked = source.RpgPublishersLocked;
    }

    private async Task MoveVolumeReferencesAsync(int[] sourceVolumeIds, int primaryVolumeId, CancellationToken cancellationToken)
    {
        var context = unitOfWork.DataContext;

        var progresses = await context.AppUserProgresses
            .Where(item => sourceVolumeIds.Contains(item.VolumeId)).ToListAsync(cancellationToken);
        foreach (var item in progresses) item.VolumeId = primaryVolumeId;

        var bookmarks = await context.AppUserBookmark
            .Where(item => sourceVolumeIds.Contains(item.VolumeId)).ToListAsync(cancellationToken);
        foreach (var item in bookmarks) item.VolumeId = primaryVolumeId;

        var annotations = await context.AppUserAnnotation
            .Where(item => sourceVolumeIds.Contains(item.VolumeId)).ToListAsync(cancellationToken);
        foreach (var item in annotations) item.VolumeId = primaryVolumeId;

        var tableOfContents = await context.AppUserTableOfContent
            .Where(item => sourceVolumeIds.Contains(item.VolumeId)).ToListAsync(cancellationToken);
        foreach (var item in tableOfContents) item.VolumeId = primaryVolumeId;

        var readingActivities = await context.AppUserReadingSessionActivityData
            .Where(item => sourceVolumeIds.Contains(item.VolumeId)).ToListAsync(cancellationToken);
        foreach (var item in readingActivities) item.VolumeId = primaryVolumeId;

        var readingListItems = await context.ReadingListItem
            .Where(item => sourceVolumeIds.Contains(item.VolumeId)).ToListAsync(cancellationToken);
        foreach (var item in readingListItems)
        {
            item.VolumeId = primaryVolumeId;
        }

        var remapRules = await context.ReadingListRemapRule
            .Where(item => item.VolumeId.HasValue && sourceVolumeIds.Contains(item.VolumeId.Value))
            .ToListAsync(cancellationToken);
        foreach (var item in remapRules) item.VolumeId = primaryVolumeId;
    }

    private static void MergeSharedBibliography(Volume primary, IReadOnlyCollection<Volume> sources,
        IReadOnlyCollection<RpgMaterialType> materialTypes)
    {
        if (primary.RpgMaterialType == RpgMaterialType.Unclassified && materialTypes.Count == 1)
        {
            primary.RpgMaterialType = materialTypes.Single();
        }

        if (string.IsNullOrWhiteSpace(primary.Summary))
        {
            var source = sources.FirstOrDefault(volume => !string.IsNullOrWhiteSpace(volume.Summary));
            if (source is not null)
            {
                primary.Summary = source.Summary;
                primary.SummaryLocked = source.SummaryLocked;
            }
        }

        if (!primary.RpgPublicationYear.HasValue)
        {
            var source = sources.FirstOrDefault(volume => volume.RpgPublicationYear.HasValue);
            if (source is not null)
            {
                primary.RpgPublicationYear = source.RpgPublicationYear;
                primary.RpgPublicationYearLocked = source.RpgPublicationYearLocked;
            }
        }

        if (string.IsNullOrWhiteSpace(primary.CoverImage))
        {
            var source = sources.FirstOrDefault(volume => !string.IsNullOrWhiteSpace(volume.CoverImage));
            if (source is not null)
            {
                primary.CoverImage = source.CoverImage;
                primary.CoverImageLocked = source.CoverImageLocked;
            }
        }

        if (!primary.RpgWritersLocked)
        {
            primary.RpgWriters = MergeNames(primary.RpgWriters, sources.SelectMany(volume => volume.RpgWriters));
            primary.RpgWritersLocked = sources.Any(volume => volume.RpgWritersLocked);
        }

        if (!primary.RpgPublishersLocked)
        {
            primary.RpgPublishers = MergeNames(primary.RpgPublishers, sources.SelectMany(volume => volume.RpgPublishers));
            primary.RpgPublishersLocked = sources.Any(volume => volume.RpgPublishersLocked);
        }

        if (!primary.RpgGeekId.HasValue)
        {
            var source = sources.FirstOrDefault(volume => volume.RpgGeekId.HasValue);
            if (source is not null)
            {
                primary.RpgGeekId = source.RpgGeekId;
                primary.RpgGeekMatchStatus = source.RpgGeekMatchStatus;
                primary.RpgGeekLastCheckedUtc = source.RpgGeekLastCheckedUtc;
            }
        }

        if (!primary.DriveThruRpgId.HasValue)
        {
            var source = sources.FirstOrDefault(volume => volume.DriveThruRpgId.HasValue);
            if (source is not null)
            {
                primary.DriveThruRpgId = source.DriveThruRpgId;
                primary.DriveThruRpgMatchStatus = source.DriveThruRpgMatchStatus;
                primary.DriveThruRpgLastCheckedUtc = source.DriveThruRpgLastCheckedUtc;
            }
        }
    }

    private static IList<string> MergeNames(IEnumerable<string> primary, IEnumerable<string> sources) =>
        primary.Concat(sources)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool HasConflictingIds(IEnumerable<int?> ids) =>
        ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct().Skip(1).Any();

    private static RpgPublicationGroupingResult Failure(RpgPublicationGroupingError error, int primaryVolumeId) =>
        new(false, error, primaryVolumeId, Array.Empty<int>());

    private static RpgPublicationVersionSplitResult SplitFailure(RpgPublicationVersionSplitError error) =>
        new(false, error, 0);
}
