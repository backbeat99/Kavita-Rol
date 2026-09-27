using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Kavita.API.Database;
using Kavita.API.Repositories;
using Kavita.API.Services;
using Kavita.API.Services.SignalR;
using Kavita.Models.Constants;
using Kavita.Models.DTOs;
using Kavita.Models.DTOs.SignalR;
using Kavita.Models.Entities.Enums;
using Kavita.Server.Attributes;
using Kavita.Server.Helpers;
using Kavita.Services.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kavita.Server.Controllers;

public class VolumeController(IUnitOfWork unitOfWork, ILocalizationService localizationService, IEventHub eventHub,
    IDriveThruRpgMetadataService driveThruRpgMetadataService,
    IRpgGeekMetadataService rpgGeekMetadataService)
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

        ExternalMetadataIdHelper.SetExternalMetadataIds(volume, dto);
        var isRpgVolume = await unitOfWork.DataContext.Volume.AnyAsync(item => item.Id == dto.Id
            && item.Series.Library.Type == LibraryType.Rpg, HttpContext.RequestAborted);
        var wasDtrpgEligible = volume.RpgMaterialType.IsPublication();
        if (isRpgVolume)
        {
            if (dto.DriveThruRpgId is > 0)
            {
                volume.DriveThruRpgId = dto.DriveThruRpgId;
                volume.DriveThruRpgMatchStatus = DriveThruRpgMatchStatus.Linked;
                volume.DriveThruRpgLastCheckedUtc = DateTime.UtcNow;
            }
            else
            {
                volume.DriveThruRpgId = null;
                volume.DriveThruRpgMatchStatus = DriveThruRpgMatchStatus.NotSearched;
                volume.DriveThruRpgLastCheckedUtc = null;
            }

            if (dto.RpgMaterialType.HasValue && Enum.IsDefined(dto.RpgMaterialType.Value))
            {
                volume.RpgMaterialType = dto.RpgMaterialType.Value;
            }

            if (dto.RpgGeekId is > 0)
            {
                volume.RpgGeekId = dto.RpgGeekId;
                volume.RpgGeekMatchStatus = RpgGeekMatchStatus.Linked;
                volume.RpgGeekLastCheckedUtc = DateTime.UtcNow;
            }
            else
            {
                volume.RpgGeekId = null;
                volume.RpgGeekMatchStatus = RpgGeekMatchStatus.NotSearched;
                volume.RpgGeekLastCheckedUtc = null;
            }

            if (dto.NameLocked.HasValue) volume.NameLocked = dto.NameLocked.Value;
            if (dto.Summary != null) volume.Summary = dto.Summary;
            if (dto.SummaryLocked.HasValue) volume.SummaryLocked = dto.SummaryLocked.Value;
            if (dto.ReleaseDate != null) volume.ReleaseDate = dto.ReleaseDate;
            if (dto.ReleaseDateLocked.HasValue) volume.ReleaseDateLocked = dto.ReleaseDateLocked.Value;
            if (dto.Language != null) volume.Language = dto.Language;
            if (dto.LanguageLocked.HasValue) volume.LanguageLocked = dto.LanguageLocked.Value;
        }

        unitOfWork.VolumeRepository.Update(volume);

        if (unitOfWork.HasChanges() && !await unitOfWork.CommitAsync())
            return BadRequest(await localizationService.TranslateAsync(UserId, "generic-error"));

        if (isRpgVolume && !wasDtrpgEligible && volume.RpgMaterialType.IsPublication()
            && volume.DriveThruRpgId is null)
        {
            var dtrpgEnabled = await unitOfWork.DataContext.Volume.AnyAsync(item => item.Id == volume.Id
                && item.Series.Library.EnableDriveThruRpgMetadata, HttpContext.RequestAborted);
            if (dtrpgEnabled)
            {
                BackgroundJob.Enqueue<IDriveThruRpgMetadataService>(service =>
                    service.MatchUnmatchedVolumesAsync(new int[] { volume.Id }, CancellationToken.None));
            }

            var rpgGeekEnabled = await unitOfWork.DataContext.Volume.AnyAsync(item => item.Id == volume.Id
                && item.Series.Library.EnableRpgGeekMetadata, HttpContext.RequestAborted);
            if (rpgGeekEnabled)
            {
                BackgroundJob.Enqueue<IRpgGeekMetadataService>(service =>
                    service.MatchUnmatchedVolumesAsync(new int[] { volume.Id }, CancellationToken.None));
            }
        }

        return Ok(await unitOfWork.VolumeRepository.GetVolumeDtoAsync(volume.Id, UserId));
    }

    /// <summary>
    /// Re-query DriveThruRPG metadata for a Manual using its explicit product ID, or a unique title match.
    /// </summary>
    [HttpPost("drivethrurpg/refresh")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult> RefreshDriveThruRpgMetadata([FromQuery] int volumeId)
    {
        var ct = HttpContext.RequestAborted;
        var providerEnabled = await unitOfWork.DataContext.Volume.AnyAsync(volume => volume.Id == volumeId &&
            volume.Series.Library.EnableDriveThruRpgMetadata && volume.Series.Library.Type == LibraryType.Rpg, ct);
        if (!providerEnabled) return BadRequest(await localizationService.TranslateAsync(UserId, "drivethrurpg-metadata-disabled"));

        BackgroundJob.Enqueue<IDriveThruRpgMetadataService>(service =>
            service.RefreshVolumeAsync(volumeId, CancellationToken.None));
        return Accepted();
    }

    /// <summary>
    /// Search DriveThruRPG for candidate products that can be linked to this RPG publication.
    /// </summary>
    [HttpGet("drivethrurpg/candidates")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<IReadOnlyList<DriveThruRpgSearchResult>>> GetDriveThruRpgCandidates([FromQuery] int volumeId, [FromQuery] string? query)
    {
        var ct = HttpContext.RequestAborted;
        var providerEnabled = await unitOfWork.DataContext.Volume.AnyAsync(volume => volume.Id == volumeId &&
            volume.Series.Library.EnableDriveThruRpgMetadata && volume.Series.Library.Type == LibraryType.Rpg
            && volume.RpgMaterialType.IsPublication(), ct);
        if (!providerEnabled) return BadRequest(await localizationService.TranslateAsync(UserId, "drivethrurpg-metadata-disabled"));

        return Ok(await driveThruRpgMetadataService.GetCandidatesAsync(volumeId, query, ct));
    }

    /// <summary>
    /// Link an explicit DriveThruRPG product ID to this RPG publication and refresh its metadata.
    /// </summary>
    [HttpPost("drivethrurpg/link")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult> LinkDriveThruRpg([FromQuery] int volumeId, [FromQuery] int productId)
    {
        var ct = HttpContext.RequestAborted;
        if (!await driveThruRpgMetadataService.LinkVolumeAsync(volumeId, productId, ct))
            return BadRequest(await localizationService.TranslateAsync(UserId, "drivethrurpg-metadata-disabled"));

        BackgroundJob.Enqueue<IDriveThruRpgMetadataService>(service =>
            service.RefreshVolumeAsync(volumeId, CancellationToken.None));
        return Accepted();
    }

    /// <summary>
    /// Link an explicit RPGGeek item ID to this RPG publication and refresh its metadata.
    /// </summary>
    [HttpPost("rpggeek/link")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult> LinkRpgGeek([FromQuery] int volumeId, [FromQuery] int rpgGeekId)
    {
        var ct = HttpContext.RequestAborted;
        if (!await rpgGeekMetadataService.LinkVolumeAsync(volumeId, rpgGeekId, ct))
            return BadRequest(await localizationService.TranslateAsync(UserId, "rpggeek-metadata-disabled"));

        BackgroundJob.Enqueue<IRpgGeekMetadataService>(service =>
            service.RefreshVolumeAsync(volumeId, CancellationToken.None));
        return Accepted();
    }

    /// <summary>
    /// Search RPGGeek for candidate items that can be linked to this RPG publication.
    /// </summary>
    [HttpGet("rpggeek/candidates")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult<IReadOnlyList<RpgGeekSearchResult>>> GetRpgGeekCandidates([FromQuery] int volumeId, [FromQuery] string? query)
    {
        var ct = HttpContext.RequestAborted;
        var providerEnabled = await unitOfWork.DataContext.Volume.AnyAsync(volume => volume.Id == volumeId &&
            volume.Series.Library.EnableRpgGeekMetadata && volume.Series.Library.Type == LibraryType.Rpg
            && volume.RpgMaterialType.IsPublication(), ct);
        if (!providerEnabled) return BadRequest(await localizationService.TranslateAsync(UserId, "rpggeek-metadata-disabled"));

        return Ok(await rpgGeekMetadataService.GetCandidatesAsync(volumeId, query, ct));
    }

    /// <summary>
    /// Re-query RPGGeek metadata for a publication using its explicit item ID, or a unique title match.
    /// </summary>
    [HttpPost("rpggeek/refresh")]
    [Authorize(Policy = PolicyGroups.AdminPolicy)]
    public async Task<ActionResult> RefreshRpgGeekMetadata([FromQuery] int volumeId)
    {
        var ct = HttpContext.RequestAborted;
        var providerEnabled = await unitOfWork.DataContext.Volume.AnyAsync(volume => volume.Id == volumeId &&
            volume.Series.Library.EnableRpgGeekMetadata && volume.Series.Library.Type == LibraryType.Rpg, ct);
        if (!providerEnabled) return BadRequest(await localizationService.TranslateAsync(UserId, "rpggeek-metadata-disabled"));

        BackgroundJob.Enqueue<IRpgGeekMetadataService>(service =>
            service.RefreshVolumeAsync(volumeId, CancellationToken.None));
        return Accepted();
    }

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
