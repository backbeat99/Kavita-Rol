using Kavita.Models.DTOs;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Builders;
using Kavita.Services.Metadata;

namespace Kavita.Services.Tests.Metadata;

public class RpgExternalMetadataIdEditorTests
{
    [Fact]
    public void ExplicitIdsAreLinkedWithoutImportingProviderMetadata()
    {
        var volume = new VolumeBuilder("Local title").Build();
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Candidate;
        volume.DriveThruRpgMatchStatus = DriveThruRpgMatchStatus.Candidate;
        volume.Summary = "Local description";

        Assert.True(RpgExternalMetadataIdEditor.TryApply(volume, new UpdateRpgExternalMetadataIdsDto
        {
            RpgGeekId = 439529,
            DriveThruRpgId = 123456
        }));

        Assert.Equal(439529, volume.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.Linked, volume.RpgGeekMatchStatus);
        Assert.Null(volume.RpgGeekLastCheckedUtc);
        Assert.Equal(123456, volume.DriveThruRpgId);
        Assert.Equal(DriveThruRpgMatchStatus.Linked, volume.DriveThruRpgMatchStatus);
        Assert.Null(volume.DriveThruRpgLastCheckedUtc);
        Assert.Equal("Local title", volume.Name);
        Assert.Equal("Local description", volume.Summary);
    }

    [Fact]
    public void ClearingIdsResetsProviderLinks()
    {
        var volume = new VolumeBuilder("Local title").Build();
        volume.RpgGeekId = 439529;
        volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;
        volume.DriveThruRpgId = 123456;
        volume.DriveThruRpgMatchStatus = DriveThruRpgMatchStatus.Linked;

        Assert.True(RpgExternalMetadataIdEditor.TryApply(volume, new UpdateRpgExternalMetadataIdsDto()));

        Assert.Null(volume.RpgGeekId);
        Assert.Equal(RpgGeekMatchStatus.NotSearched, volume.RpgGeekMatchStatus);
        Assert.Null(volume.DriveThruRpgId);
        Assert.Equal(DriveThruRpgMatchStatus.NotSearched, volume.DriveThruRpgMatchStatus);
    }

    [Theory]
    [InlineData(0, 123456)]
    [InlineData(-1, 123456)]
    [InlineData(439529, 0)]
    [InlineData(439529, -1)]
    public void NonPositiveIdsAreRejectedWithoutPartialChanges(int rpgGeekId, int driveThruRpgId)
    {
        var volume = new VolumeBuilder("Local title").Build();
        volume.RpgGeekId = 1;
        volume.DriveThruRpgId = 2;

        Assert.False(RpgExternalMetadataIdEditor.TryApply(volume, new UpdateRpgExternalMetadataIdsDto
        {
            RpgGeekId = rpgGeekId,
            DriveThruRpgId = driveThruRpgId
        }));

        Assert.Equal(1, volume.RpgGeekId);
        Assert.Equal(2, volume.DriveThruRpgId);
    }
}
