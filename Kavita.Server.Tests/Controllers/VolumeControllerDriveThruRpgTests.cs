using System.Reflection;
using Kavita.API.Database;
using Kavita.API.Services;
using Kavita.API.Services.Metadata;
using Kavita.API.Services.SignalR;
using Kavita.Models.Constants;
using Kavita.Models.Entities.Enums;
using Kavita.Server.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;

namespace Kavita.Server.Tests.Controllers;

public class VolumeControllerDriveThruRpgTests
{
    [Fact]
    public async Task Candidate_search_does_not_link_or_import_a_product()
    {
        var metadataService = Substitute.For<IDriveThruRpgMetadataService>();
        var expected = new DriveThruRpgCandidateSearchResult(true, DriveThruRpgMetadataOperationError.None,
            DriveThruRpgMatchStatus.Candidate, [new DriveThruRpgSearchResult(123, "Test Product")]);
        metadataService.SearchCandidatesForVolumeAsync(42, "Test Product", Arg.Any<CancellationToken>())
            .Returns(expected);
        var controller = CreateController(metadataService);

        var action = await controller.SearchDriveThruRpgCandidates(42, "Test Product");

        var response = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Same(expected, response.Value);
        await metadataService.Received(1).SearchCandidatesForVolumeAsync(
            42, "Test Product", Arg.Any<CancellationToken>());
        await metadataService.DidNotReceive().LinkVolumeAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await metadataService.DidNotReceive().RefreshVolumeAsync(
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Candidate_search_link_and_refresh_require_admin_authorization()
    {
        AssertAdminEndpoint(nameof(VolumeController.SearchDriveThruRpgCandidates), "GET",
            "rpg/drivethrurpg/candidates");
        AssertAdminEndpoint(nameof(VolumeController.LinkDriveThruRpg), "POST", "rpg/drivethrurpg/link");
        AssertAdminEndpoint(nameof(VolumeController.RefreshDriveThruRpgMetadata), "POST", "rpg/drivethrurpg/refresh");
        AssertAdminEndpoint(nameof(VolumeController.GroupRpgPublicationVersions), "POST", "rpg/group-versions");
        AssertAdminEndpoint(nameof(VolumeController.SplitRpgPublicationVersion), "POST", "rpg/split-version");
    }

    private static VolumeController CreateController(IDriveThruRpgMetadataService metadataService)
    {
        var controller = new VolumeController(
            Substitute.For<IUnitOfWork>(),
            Substitute.For<ILocalizationService>(),
            Substitute.For<IEventHub>(),
            Substitute.For<IRpgMaterialClassificationService>(),
            Substitute.For<IRpgPublicationGroupingService>(),
            Substitute.For<IRpgGeekMetadataService>(),
            metadataService);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static void AssertAdminEndpoint(string methodName, string httpMethod, string route)
    {
        var action = typeof(VolumeController).GetMethod(methodName);
        Assert.NotNull(action);
        Assert.Equal(PolicyGroups.AdminPolicy, action.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        var routeTemplate = httpMethod == "GET"
            ? action.GetCustomAttribute<HttpGetAttribute>()?.Template
            : action.GetCustomAttribute<HttpPostAttribute>()?.Template;
        Assert.Equal(route, routeTemplate);
    }
}
