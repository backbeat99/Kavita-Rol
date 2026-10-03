using Kavita.Models.Entities.Enums;
using Kavita.Services.Scanner;

namespace Kavita.Services.Tests;

public class ProcessSeriesTests
{
    // TODO: Implement

    #region UpdateSeriesMetadata

    [Theory]
    [InlineData(LibraryType.Rpg, false, false)]
    [InlineData(LibraryType.Rpg, true, false)]
    [InlineData(LibraryType.Manga, false, true)]
    [InlineData(LibraryType.Manga, true, false)]
    public void ShouldInheritChapterTags_SeparatesRpgGameAndMaterialTags(
        LibraryType libraryType, bool tagsLocked, bool expected)
    {
        Assert.Equal(expected, ProcessSeries.ShouldInheritChapterTags(libraryType, tagsLocked));
    }

    #endregion

    #region UpdateVolumes



    #endregion

    #region UpdateChapters



    #endregion

    #region AddOrUpdateFileForChapter



    #endregion

    #region UpdateChapterFromComicInfo

    // public void UpdateChapterFromComicInfo_()
    // {
    //     // TODO: Do this
    //     var file = Path.Join(Directory.GetCurrentDirectory(), "../../../Test Data/ScannerService/Library/Manga/Hajime no Ippo/Hajime no Ippo Chapter 1.cbz");
    //     // Chapter and ComicInfo
    //     var chapter = new ChapterBuilder("1")
    //         .WithId(0)
    //         .WithFile(new MangaFileBuilder(file, MangaFormat.Archive).Build())
    //         .Build();
    //
    //     var ps = new ProcessSeries(Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<ProcessSeries>>(),
    //         Substitute.For<IEventHub>(), Substitute.For<IDirectoryService>()
    //         , Substitute.For<ICacheHelper>(), Substitute.For<IReadingItemService>(), Substitute.For<IFileService>(),
    //         Substitute.For<IMetadataService>(),
    //         Substitute.For<IWordCountAnalyzerService>(),
    //         Substitute.For<ICollectionTagService>(), Substitute.For<IReadingListService>());
    //
    //     ps.UpdateChapterFromComicInfo(chapter, new ComicInfo()
    //     {
    //
    //     });
    // }

    #endregion
}
