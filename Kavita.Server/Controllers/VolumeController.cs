using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Kavita.API.Database;
using Kavita.API.Repositories;
using Kavita.API.Services;
using Kavita.API.Services.Metadata;
using Kavita.API.Services.SignalR;
using Kavita.Models.Constants;
using Kavita.Models.DTOs;
using Kavita.Models.DTOs.SignalR;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Metadata;
using Kavita.Server.Attributes;
using Kavita.Server.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kavita.Server.Controllers;

public class VolumeController(IUnitOfWork unitOfWork, ILocalizationService localizationService, IEventHub eventHub,
    IRpgMaterialClassificationService rpgMaterialClassificationService,
    IRpgGeekMetadataService rpgGeekMetadataService,
    IDriveThruRpgMetadataService driveThruRpgMetadataService)
    : BaseApiController
{
    /// <summary>
    /// Returns the appropriate Volume
    /// </summary>
    /// <param name="volumeId"></param>
    /// <returns></returns>
    [VolumeAccess]
    [HttpGet]
    public async Task<ActionResult<VolumeDto?>> GetVolume(int volumeId)
    {
        return Ok(await unitOfWork.VolumeRepository.GetVolumeDtoAsync(volumeId, UserId));
    }

    /// <summary>
    /// Updates the information on the Volume
    /// </summary>
    /// <param name="dto"></param>
    /// <returns></returns>
    [HttpPost("update")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<VolumeDto>> UpdateVolume(UpdateVolumeDto dto)
    {
        var volume = await unitOfWork.VolumeRepository.GetVolumeByIdAsync(dto.Id);
        if (volume == null) return BadRequest(await localizationService.TranslateAsync(UserId, "volume-doesnt-exist"));

        if (dto.RpgBibliography is not null)
        {
            var libraryType = await unitOfWork.DataContext.Volume
                .Where(item => item.Id == volume.Id)
                .Select(item => item.Series.Library.Type)
                .FirstOrDefaultAsync(HttpContext.RequestAborted);
            if (libraryType != LibraryType.Rpg)
                return BadRequest(await localizationService.TranslateAsync(UserId, "rpggeek-not-rpg-library"));
            if (!RpgBibliographyEditor.TryApply(volume, dto.RpgBibliography))
                return BadRequest(await localizationService.TranslateAsync(UserId, "rpg-bibliography-invalid"));
        }

        ExternalMetadataIdHelper.SetExternalMetadataIds(volume, dto);

        unitOfWork.VolumeRepository.Update(volume);

        if (unitOfWork.HasChanges() && !await unitOfWork.CommitAsync())
            return BadRequest(await localizationService.TranslateAsync(UserId, "generic-error"));

        return Ok(await unitOfWork.VolumeRepository.GetVolumeDtoAsync(volume.Id, UserId));
    }

    /// <summary>
    /// Classify multiple RPG catalog items atomically. Publications enter candidate search only;
    /// the background search never links a result or applies external fields.
    /// </summary>
    [HttpPost("rpg/classify-batch")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<IReadOnlyList<int>>> ClassifyRpgMaterialBatch(UpdateRpgMaterialTypeBatchDto dto)
    {
        var result = await rpgMaterialClassificationService.ClassifyBatchAsync(
            dto.SeriesId,
            dto.Items.Select(item => new RpgMaterialClassificationUpdate(item.VolumeId, item.MaterialType)).ToArray(),
            HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            var key = result.Error switch
            {
                RpgMaterialClassificationError.EmptyBatch => "rpg-classification-empty",
                RpgMaterialClassificationError.DuplicateIds => "rpg-classification-duplicate-items",
                RpgMaterialClassificationError.InvalidType => "rpg-classification-invalid-type",
                RpgMaterialClassificationError.VolumeNotFound => "volume-doesnt-exist",
                RpgMaterialClassificationError.WrongSeries => "rpg-classification-wrong-game",
                RpgMaterialClassificationError.NotRpgLibrary => "rpg-classification-not-rpg-library",
                _ => "generic-error"
            };
            return BadRequest(await localizationService.TranslateAsync(UserId, key));
        }

        foreach (var volumeId in result.RpgGeekSearchVolumeIds)
        {
            BackgroundJob.Enqueue<IRpgGeekMetadataService>(service =>
                service.SearchCandidatesAsync(volumeId, CancellationToken.None));
        }

        if (result.DriveThruRpgMatchVolumeIds.Count > 0)
        {
            BackgroundJob.Enqueue<IDriveThruRpgMetadataService>(service =>
                service.MatchUnmatchedVolumesAsync(result.DriveThruRpgMatchVolumeIds.ToList(), CancellationToken.None));
        }

        return Accepted(result.RpgGeekSearchVolumeIds);
    }

    [HttpGet("rpg/geek/candidates")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<RpgGeekCandidateSearchResult>> SearchRpgGeekCandidates(
        int volumeId, string? query = null, bool forceRefresh = false)
    {
        var result = await rpgGeekMetadataService.SearchCandidatesForVolumeAsync(
            volumeId, query, HttpContext.RequestAborted, forceRefresh);
        if (!result.Succeeded)
        {
            return BadRequest(await localizationService.TranslateAsync(UserId, RpgGeekErrorKey(result.Error)));
        }

        return Ok(result);
    }

    [HttpGet("rpg/geek/preview")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<RpgGeekCandidatePreviewResult>> PreviewRpgGeekCandidate(
        int volumeId, int productId, bool forceRefresh = false)
    {
        var result = await rpgGeekMetadataService.PreviewCandidateAsync(
            volumeId, productId, HttpContext.RequestAborted, forceRefresh);
        if (!result.Succeeded)
        {
            return BadRequest(await localizationService.TranslateAsync(UserId, RpgGeekErrorKey(result.Error)));
        }

        return Ok(result.Preview);
    }

    [HttpPost("rpg/geek/apply")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<RpgGeekCandidateApplyResult>> ApplyRpgGeekCandidate(ApplyRpgGeekCandidateDto dto)
    {
        var result = await rpgGeekMetadataService.ApplyCandidateAsync(new RpgGeekCandidateApplyRequest(
                dto.VolumeId, dto.ProductId, dto.PreviewFingerprint, dto.ReplaceTitle, dto.ReplaceSummary,
                dto.ReplaceYear, dto.ReplaceWriters, dto.ReplacePublishers, dto.ReplaceCover),
                HttpContext.RequestAborted);

        if (result.Error == RpgGeekMetadataOperationError.PreviewChanged)
        {
            return Conflict(new
            {
                Message = await localizationService.TranslateAsync(UserId, "rpggeek-preview-changed"),
                result.UpdatedPreview
            });
        }

        if (!result.Succeeded)
        {
            return BadRequest(await localizationService.TranslateAsync(UserId, RpgGeekErrorKey(result.Error)));
        }

        return Ok(true);
    }

    [HttpPost("rpg/geek/apply-batch")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<bool>> ApplyRpgGeekCandidatesBatch(ApplyRpgGeekCandidatesBatchDto dto)
    {
        var result = await rpgGeekMetadataService.ApplyCandidatesBatchAsync(dto.SeriesId,
            dto.Items.Select(item => new RpgGeekCandidateApplyRequest(
                item.VolumeId, item.ProductId, item.PreviewFingerprint, item.ReplaceTitle, item.ReplaceSummary,
                item.ReplaceYear, item.ReplaceWriters, item.ReplacePublishers, item.ReplaceCover)).ToArray(),
            HttpContext.RequestAborted);

        if (result.Error == RpgGeekMetadataOperationError.PreviewChanged)
        {
            return Conflict(new
            {
                Message = await localizationService.TranslateAsync(UserId, "rpggeek-preview-changed"),
                result.UpdatedPreviews
            });
        }

        if (!result.Succeeded)
        {
            return BadRequest(await localizationService.TranslateAsync(UserId, RpgGeekErrorKey(result.Error)));
        }

        return Ok(true);
    }

    [HttpGet("rpg/drivethrurpg/candidates")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<DriveThruRpgCandidateSearchResult>> SearchDriveThruRpgCandidates(
        int volumeId, string? query = null)
    {
        var result = await driveThruRpgMetadataService.SearchCandidatesForVolumeAsync(
            volumeId, query, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            return BadRequest(await localizationService.TranslateAsync(UserId, DriveThruRpgErrorKey(result.Error)));
        }

        return Ok(result);
    }

    [HttpPost("rpg/drivethrurpg/link")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<bool>> LinkDriveThruRpg(int volumeId, int productId)
    {
        var result = await driveThruRpgMetadataService.LinkVolumeAsync(
            volumeId, productId, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            return BadRequest(await localizationService.TranslateAsync(UserId, DriveThruRpgErrorKey(result.Error)));
        }

        BackgroundJob.Enqueue<IDriveThruRpgMetadataService>(service =>
            service.RefreshVolumeAsync(volumeId, CancellationToken.None));
        return Accepted(true);
    }

    [HttpPost("rpg/drivethrurpg/refresh")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult> RefreshDriveThruRpgMetadata(int volumeId)
    {
        var error = await GetDriveThruRpgEligibilityErrorAsync(volumeId, HttpContext.RequestAborted);
        if (error != DriveThruRpgMetadataOperationError.None)
        {
            return BadRequest(await localizationService.TranslateAsync(UserId, DriveThruRpgErrorKey(error)));
        }

        BackgroundJob.Enqueue<IDriveThruRpgMetadataService>(service =>
            service.RefreshVolumeAsync(volumeId, CancellationToken.None));
        return Accepted();
    }

    private async Task<DriveThruRpgMetadataOperationError> GetDriveThruRpgEligibilityErrorAsync(
        int volumeId, CancellationToken cancellationToken)
    {
        var volume = await unitOfWork.DataContext.Volume
            .Include(item => item.Series)
            .ThenInclude(series => series.Library)
            .FirstOrDefaultAsync(item => item.Id == volumeId, cancellationToken);
        if (volume is null) return DriveThruRpgMetadataOperationError.VolumeNotFound;
        if (volume.Series.Library.Type != LibraryType.Rpg) return DriveThruRpgMetadataOperationError.NotRpgLibrary;
        if (!volume.RpgMaterialType.IsPublication()) return DriveThruRpgMetadataOperationError.NotPublication;
        return volume.Series.Library.EnableDriveThruRpgMetadata
            ? DriveThruRpgMetadataOperationError.None
            : DriveThruRpgMetadataOperationError.ProviderDisabled;
    }

    private static string DriveThruRpgErrorKey(DriveThruRpgMetadataOperationError error) => error switch
    {
        DriveThruRpgMetadataOperationError.VolumeNotFound => "volume-doesnt-exist",
        DriveThruRpgMetadataOperationError.NotRpgLibrary => "drivethrurpg-not-rpg-library",
        DriveThruRpgMetadataOperationError.NotPublication => "drivethrurpg-not-publication",
        DriveThruRpgMetadataOperationError.ProviderDisabled => "drivethrurpg-provider-disabled",
        DriveThruRpgMetadataOperationError.QueryRequired => "drivethrurpg-query-required",
        DriveThruRpgMetadataOperationError.InvalidProductId => "drivethrurpg-invalid-product-id",
        DriveThruRpgMetadataOperationError.ProviderRequestFailed => "drivethrurpg-request-failed",
        _ => "generic-error"
    };

    private static string RpgGeekErrorKey(RpgGeekMetadataOperationError error) => error switch
    {
        RpgGeekMetadataOperationError.VolumeNotFound => "volume-doesnt-exist",
        RpgGeekMetadataOperationError.NotRpgLibrary => "rpggeek-not-rpg-library",
        RpgGeekMetadataOperationError.NotPublication => "rpggeek-not-publication",
        RpgGeekMetadataOperationError.ProviderDisabled => "rpggeek-provider-disabled",
        RpgGeekMetadataOperationError.TokenMissing => "rpggeek-token-missing",
        RpgGeekMetadataOperationError.QueryRequired => "rpggeek-query-required",
        RpgGeekMetadataOperationError.ProductNotFound => "rpggeek-product-not-found",
        RpgGeekMetadataOperationError.PreviewChanged => "rpggeek-preview-changed",
        RpgGeekMetadataOperationError.BatchEmpty => "rpggeek-batch-empty",
        RpgGeekMetadataOperationError.DuplicateVolumeIds => "rpggeek-batch-duplicate",
        RpgGeekMetadataOperationError.WrongSeries => "rpggeek-batch-wrong-game",
        RpgGeekMetadataOperationError.AmbiguousRequiresIndividualReview => "rpggeek-ambiguous-individual",
        RpgGeekMetadataOperationError.CandidateNotReady => "rpggeek-candidate-not-ready",
        _ => "rpggeek-request-failed"
    };

    /// <summary>
    /// Delete the Volume from the DB
    /// </summary>
    /// <param name="volumeId"></param>
    /// <returns></returns>
    [HttpDelete]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<bool>> DeleteVolume(int volumeId)
    {
        var volume = await unitOfWork.VolumeRepository.GetVolumeByIdAsync(volumeId,
            VolumeIncludes.Chapters | VolumeIncludes.People | VolumeIncludes.Tags);
        if (volume == null)
            return BadRequest(await localizationService.TranslateAsync(UserId, "volume-doesnt-exist"));

        unitOfWork.VolumeRepository.Remove(volume);

        if (await unitOfWork.CommitAsync())
        {
            await eventHub.SendMessageAsync(MessageFactory.VolumeRemoved, MessageFactory.VolumeRemovedEvent(volume.Id, volume.SeriesId), false);
            return Ok(true);
        }

        return Ok(false);
    }

    /// <summary>
    /// Delete multiple Volumes from the DB
    /// </summary>
    /// <param name="volumesIds"></param>
    /// <returns></returns>
    [HttpPost("multiple")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<bool>> DeleteMultipleVolumes(int[] volumesIds)
    {
        var volumes = await unitOfWork.VolumeRepository.GetVolumesById(volumesIds);
        if (volumes.Count != volumesIds.Length)
        {
            return BadRequest(await localizationService.TranslateAsync(UserId, "volume-doesnt-exist"));
        }

        unitOfWork.VolumeRepository.Remove(volumes);

        if (!await unitOfWork.CommitAsync())
        {
            return Ok(false);
        }

        foreach (var volume in volumes)
        {
            await eventHub.SendMessageAsync(MessageFactory.VolumeRemoved, MessageFactory.VolumeRemovedEvent(volume.Id, volume.SeriesId), false);
        }

        return Ok(true);
    }
}
