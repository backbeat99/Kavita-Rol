using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kavita.Services.Metadata;

namespace Kavita.API.Services;

public interface IDriveThruRpgMetadataService
{
    Task MatchUnmatchedChaptersInLibraryAsync(int libraryId, CancellationToken ct = default);
    Task MatchUnmatchedChaptersAsync(IList<int> chapterIds, CancellationToken ct = default);
    Task RefreshChapterAsync(int chapterId, CancellationToken ct = default);
    Task MatchUnmatchedVolumesAsync(IList<int> volumeIds, CancellationToken ct = default);
    Task RefreshVolumeAsync(int volumeId, CancellationToken ct = default);
    Task<IReadOnlyList<DriveThruRpgSearchResult>> GetCandidatesAsync(int volumeId, string? query = null, CancellationToken ct = default);
    Task<bool> LinkVolumeAsync(int volumeId, int productId, CancellationToken ct = default);
}
