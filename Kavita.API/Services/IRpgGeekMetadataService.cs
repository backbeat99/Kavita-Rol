using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kavita.Services.Metadata;

namespace Kavita.API.Services;

public interface IRpgGeekMetadataService
{
    Task MatchUnmatchedVolumesInLibraryAsync(int libraryId, CancellationToken ct = default);
    Task MatchUnmatchedVolumesAsync(IList<int> volumeIds, CancellationToken ct = default);
    Task RefreshVolumeAsync(int volumeId, CancellationToken ct = default);
    Task<IReadOnlyList<RpgGeekSearchResult>> GetCandidatesAsync(int volumeId, string? query = null, CancellationToken ct = default);
    Task<bool> LinkVolumeAsync(int volumeId, int rpgGeekId, CancellationToken ct = default);
}
