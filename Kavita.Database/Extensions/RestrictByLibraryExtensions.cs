using System.Linq;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Person;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Entities;

namespace Kavita.Database.Extensions;

public static class RestrictByLibraryExtensions
{
    /// <summary>Match library and age on the same credit; independent matches can expose a restricted person.</summary>
    public static IQueryable<Person> RestrictByAccessibleCredits(this IQueryable<Person> query,
        IQueryable<int> userLibs, AgeRestriction restriction)
    {
        if (restriction.AgeRating == AgeRating.NotApplicable) return query.RestrictByLibrary(userLibs);
        if (restriction.IncludeUnknowns)
            return query.Where(p =>
                p.ChapterPeople.Any(cp => userLibs.Contains(cp.Chapter.Volume.Series.LibraryId) && cp.Chapter.AgeRating <= restriction.AgeRating) ||
                p.SeriesMetadataPeople.Any(sm => userLibs.Contains(sm.SeriesMetadata.Series.LibraryId) && sm.SeriesMetadata.AgeRating <= restriction.AgeRating) ||
                p.VolumePeople.Any(vp => userLibs.Contains(vp.Volume.Series.LibraryId) && vp.Volume.Series.Metadata.AgeRating <= restriction.AgeRating));

        return query.Where(p =>
            p.ChapterPeople.Any(cp => userLibs.Contains(cp.Chapter.Volume.Series.LibraryId) && cp.Chapter.AgeRating <= restriction.AgeRating && cp.Chapter.AgeRating != AgeRating.Unknown) ||
            p.SeriesMetadataPeople.Any(sm => userLibs.Contains(sm.SeriesMetadata.Series.LibraryId) && sm.SeriesMetadata.AgeRating <= restriction.AgeRating && sm.SeriesMetadata.AgeRating != AgeRating.Unknown) ||
            p.VolumePeople.Any(vp => userLibs.Contains(vp.Volume.Series.LibraryId) && vp.Volume.Series.Metadata.AgeRating <= restriction.AgeRating && vp.Volume.Series.Metadata.AgeRating != AgeRating.Unknown));
    }

    public static IQueryable<Person> RestrictByLibrary(this IQueryable<Person> query, IQueryable<int> userLibs)
    {
        return query.Where(p =>
            p.ChapterPeople.Any(cp => userLibs.Contains(cp.Chapter.Volume.Series.LibraryId)) ||
            p.SeriesMetadataPeople.Any(sm => userLibs.Contains(sm.SeriesMetadata.Series.LibraryId)) ||
            p.VolumePeople.Any(vp => userLibs.Contains(vp.Volume.Series.LibraryId)));
    }

    public static IQueryable<Chapter> RestrictByLibrary(this IQueryable<Chapter> query, IQueryable<int> userLibs)
    {
        return query.Where(cp => userLibs.Contains(cp.Volume.Series.LibraryId));
    }

    public static IQueryable<SeriesMetadataPeople> RestrictByLibrary(this IQueryable<SeriesMetadataPeople> query, IQueryable<int> userLibs)
    {
        return query.Where(sm => userLibs.Contains(sm.SeriesMetadata.Series.LibraryId));
    }

    public static IQueryable<ChapterPeople> RestrictByLibrary(this IQueryable<ChapterPeople> query, IQueryable<int> userLibs)
    {
        return query.Where(cp => userLibs.Contains(cp.Chapter.Volume.Series.LibraryId));
    }
}
