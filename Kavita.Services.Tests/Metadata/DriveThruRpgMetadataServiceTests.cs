using System;
using System.Collections.Generic;
using Kavita.Models.Entities;
using Kavita.Services.Metadata;
using Xunit;

namespace Kavita.Services.Tests.Metadata;

public class DriveThruRpgMetadataServiceTests
{
    [Fact]
    public void ApplyChapterMetadataFields_RespectsExistingLocks()
    {
        var releaseDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var chapter = new Chapter
        {
            Range = "1",
            TitleName = "Custom title",
            Summary = "Custom summary",
            ReleaseDate = releaseDate,
            Language = "es",
            TitleNameLocked = true,
            SummaryLocked = true,
            ReleaseDateLocked = true,
            LanguageLocked = true
        };
        var product = new DriveThruRpgProduct(123, "Product title", [], "Product summary", null,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, "en");

        DriveThruRpgMetadataService.ApplyChapterMetadataFields(chapter, product);

        Assert.Equal("Custom title", chapter.TitleName);
        Assert.Equal("Custom summary", chapter.Summary);
        Assert.Equal(releaseDate, chapter.ReleaseDate);
        Assert.Equal("es", chapter.Language);
        Assert.Equal(123, chapter.DriveThruRpgId);
    }

    [Fact]
    public void ApplyChapterMetadataFields_UpdatesUnlockedFields()
    {
        var chapter = new Chapter { Range = "1", TitleName = "Old title", Summary = "Old summary", Language = "" };
        var releaseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var product = new DriveThruRpgProduct(456, "New title", [], "New summary", null,
            releaseDate, null, "en");

        DriveThruRpgMetadataService.ApplyChapterMetadataFields(chapter, product);

        Assert.Equal("New title", chapter.TitleName);
        Assert.Equal("New summary", chapter.Summary);
        Assert.Equal(releaseDate, chapter.ReleaseDate);
        Assert.Equal("en", chapter.Language);
        Assert.Equal(456, chapter.DriveThruRpgId);
    }

    [Fact]
    public void ApplySharedMetadataFields_RespectsVersionLocksAndLeavesIdentityForManual()
    {
        var releaseDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var chapter = new Chapter
        {
            Range = "PDF (Pages)",
            Summary = "Custom summary",
            SummaryLocked = true,
            ReleaseDate = releaseDate,
            ReleaseDateLocked = true,
            Language = "es",
            LanguageLocked = true,
            DriveThruRpgId = 123
        };
        var product = new DriveThruRpgProduct(456, "Manual title", [], "Product summary", null,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, "en");

        DriveThruRpgMetadataService.ApplySharedMetadataFields(chapter, product);

        Assert.Equal("Custom summary", chapter.Summary);
        Assert.Equal(releaseDate, chapter.ReleaseDate);
        Assert.Equal("es", chapter.Language);
        Assert.Equal(123, chapter.DriveThruRpgId);
        Assert.Equal("PDF (Pages)", chapter.Range);
    }

    [Fact]
    public void BuildSearchTitleCandidates_IncludesGenericDashPrefixesMostSpecificFirst()
    {
        var candidates = DriveThruRpgMetadataService.BuildSearchTitleCandidates("Public Access - Core Rulebook - Pages");

        Assert.Equal(new[]
        {
            "Public Access - Core Rulebook - Pages",
            "Public Access - Core Rulebook",
            "Public Access"
        }, candidates);

        var taglineCandidates = DriveThruRpgMetadataService.BuildSearchTitleCandidates(
            "Frontier Scum - Acid Western Roleplaying");

        Assert.Equal(new[]
        {
            "Frontier Scum - Acid Western Roleplaying",
            "Frontier Scum"
        }, taglineCandidates);
    }

    [Fact]
    public void BuildSearchTitleCandidates_DoesNotStripSpecificProductSegments()
    {
        var candidates = DriveThruRpgMetadataService.BuildSearchTitleCandidates("Frontier Scum - Carnival");

        Assert.Equal(new[] { "Frontier Scum - Carnival" }, candidates);
    }

    [Fact]
    public void FindTitleMatch_DistinguishesMissingPrefixFromAmbiguousExactTitle()
    {
        var prefixResults = new List<DriveThruRpgSearchResult>
        {
            new(403322, "Frontier Scum")
        };
        var prefixMatch = DriveThruRpgMetadataService.FindTitleMatch(
            "Frontier Scum - Acid Western Roleplaying", prefixResults);

        Assert.Null(prefixMatch.Match);
        Assert.False(prefixMatch.IsAmbiguous);

        var ambiguousResults = new List<DriveThruRpgSearchResult>
        {
            new(123, "The Dark Eye - Core Rules"),
            new(456, "The Dark Eye Core Rules")
        };
        var ambiguousMatch = DriveThruRpgMetadataService.FindTitleMatch("The Dark Eye Core Rules", ambiguousResults);

        Assert.Null(ambiguousMatch.Match);
        Assert.True(ambiguousMatch.IsAmbiguous);
    }

    [Fact]
    public void FindUniqueMatch_ReturnsSingleNormalizedExactTitle()
    {
        var results = new List<DriveThruRpgSearchResult>
        {
            new(123, "Dragonbane: Core Rulebook"),
            new(456, "Dragonbane Bestiary")
        };

        var match = DriveThruRpgMetadataService.FindUniqueMatch("Dragonbane - Core Rulebook", results);

        Assert.NotNull(match);
        Assert.Equal(123, match.ProductId);
    }

    [Fact]
    public void FindUniqueMatch_RejectsAmbiguousExactTitles()
    {
        var results = new List<DriveThruRpgSearchResult>
        {
            new(123, "The Dark Eye - Core Rules"),
            new(456, "The Dark Eye Core Rules")
        };

        var match = DriveThruRpgMetadataService.FindUniqueMatch("The Dark Eye Core Rules", results);

        Assert.Null(match);
    }

    [Fact]
    public void FindUniqueMatch_RejectsFuzzyTitlesAndFantasyGroundsListings()
    {
        var results = new List<DriveThruRpgSearchResult>
        {
            new(123, "Dragonbane Core Rulebook (Fantasy Grounds)"),
            new(456, "Dragonbane Player Guide")
        };

        var match = DriveThruRpgMetadataService.FindUniqueMatch("Dragonbane Core Rulebook", results);

        Assert.Null(match);
    }
}
