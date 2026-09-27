using System;
using System.Collections.Generic;

namespace Kavita.API.Services.Metadata;

public sealed record DriveThruRpgSearchResult(int ProductId, string Title);

public sealed record DriveThruRpgProduct(
    int ProductId,
    string Title,
    IReadOnlyList<string> Authors,
    string Description,
    string? Publisher,
    DateTime? ReleaseDate,
    string? CoverUrl,
    string? LanguageCode);
