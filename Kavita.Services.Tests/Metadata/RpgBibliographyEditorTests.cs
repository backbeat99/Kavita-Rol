using System.Collections.Generic;
using Kavita.Models.DTOs;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Builders;
using Kavita.Services.Metadata;
using Xunit;

namespace Kavita.Services.Tests.Metadata;

public class RpgBibliographyEditorTests
{
    [Fact]
    public void CorrectedTitleAndManualFieldsAreSavedWithoutLinkingOrChangingIdentity()
    {
        var volume = new VolumeBuilder("Core Book").Build();
        volume.LookupName = "Core Book";
        volume.RpgMaterialType = RpgMaterialType.CoreManual;
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.NoMatch;
        var dto = new UpdateRpgBibliographyDto
        {
            Name = "  Pirate Borg Core Book  ", NameLocked = true,
            Summary = "  A pirate RPG  ", SummaryLocked = true,
            RpgPublicationYear = 2022, RpgPublicationYearLocked = true,
            RpgWriters = ["Creator", "creator", " Designer "], RpgWritersLocked = true,
            RpgPublishers = ["Publisher"], RpgPublishersLocked = true
        };

        Assert.True(RpgBibliographyEditor.TryApply(volume, dto));
        Assert.Equal("Pirate Borg Core Book", volume.Name);
        Assert.Equal("Core Book", volume.LookupName);
        Assert.True(volume.NameLocked);
        Assert.Equal("A pirate RPG", volume.Summary);
        Assert.Equal(2022, volume.RpgPublicationYear);
        Assert.Equal(["Creator", "Designer"], volume.RpgWriters);
        Assert.Equal(["Publisher"], volume.RpgPublishers);
        Assert.Null(volume.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.NotSearched, volume.RpgGeekMatchStatus);
    }

    [Fact]
    public void ChangingTitleDoesNotUndoConfirmedRpgGeekLink()
    {
        var volume = new VolumeBuilder("Old title").Build();
        volume.RpgGeekId = 123;
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;

        Assert.True(RpgBibliographyEditor.TryApply(volume, Request("Better title")));
        Assert.Equal(123, volume.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.Linked, volume.RpgGeekMatchStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTitleIsRejectedWithoutChangingAnyFields(string name)
    {
        var volume = new VolumeBuilder("Original").Build();
        volume.RpgPublicationYear = 2020;
        var invalid = Request(name) with { RpgPublicationYear = 2024 };

        Assert.False(RpgBibliographyEditor.TryApply(volume, invalid));
        Assert.Equal("Original", volume.Name);
        Assert.Equal(2020, volume.RpgPublicationYear);
    }

    [Fact]
    public void InvalidYearOrNamesAreRejectedBeforeAnyChange()
    {
        var volume = new VolumeBuilder("Original").Build();
        Assert.False(RpgBibliographyEditor.TryApply(volume, Request("New") with { RpgPublicationYear = 22 }));
        Assert.False(RpgBibliographyEditor.TryApply(volume, Request("New") with { RpgWriters = new List<string> { new('x', 201) } }));
        Assert.Equal("Original", volume.Name);
    }

    private static UpdateRpgBibliographyDto Request(string name) => new()
    {
        Name = name,
        RpgWriters = [],
        RpgPublishers = []
    };
}
