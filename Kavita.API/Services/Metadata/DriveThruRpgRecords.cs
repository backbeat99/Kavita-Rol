using System;
using System.Collections.Generic;

namespace Kavita.Services.Metadata;

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

public sealed record RpgGeekSearchResult(int Id, string Name, int? YearPublished);

public sealed record RpgGeekProduct(
    int Id,
    string Title,
    int? YearPublished,
    string Description,
    IReadOnlyList<string> Designers,
    IReadOnlyList<string> Publishers,
    string? ImageUrl);
