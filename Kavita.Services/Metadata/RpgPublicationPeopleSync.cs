using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kavita.API.Database;
using Kavita.Common.Extensions;
using Kavita.Models.Builders;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Entities.Person;
using Microsoft.EntityFrameworkCore;

namespace Kavita.Services.Metadata;

/// <summary>Keep publication-level Writer credits in People without assigning them to versions or games.</summary>
public static class RpgPublicationPeopleSync
{
    public static async Task SyncAsync(Volume volume, IUnitOfWork unitOfWork, CancellationToken ct = default)
    {
        var desiredNames = volume.RpgMaterialType.IsPublication()
            ? volume.RpgWriters.Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        var normalized = desiredNames.Select(name => name.ToNormalized())
            .Where(name => name.Length > 0).Distinct().ToList();
        var existingPeople = await unitOfWork.PersonRepository.GetPeopleByNames(normalized, ct: ct);
        var byName = new Dictionary<string, Person>();
        // Batch confirmations can introduce the same new person on several publications before committing.
        foreach (var person in existingPeople.Concat(unitOfWork.DataContext.Person.Local).Distinct())
        {
            byName.TryAdd(person.NormalizedName, person);
            foreach (var alias in person.Aliases) byName.TryAdd(alias.NormalizedAlias, person);
        }

        var desired = new List<Person>();
        foreach (var name in desiredNames)
        {
            var key = name.ToNormalized();
            if (key.Length == 0) continue;
            if (!byName.TryGetValue(key, out var person))
            {
                person = new PersonBuilder(name).Build();
                unitOfWork.DataContext.Person.Add(person);
                byName.Add(key, person);
            }
            if (!desired.Contains(person)) desired.Add(person);
        }

        var links = await unitOfWork.DataContext.VolumePeople
            .Where(link => link.VolumeId == volume.Id && link.Role == PersonRole.Writer)
            .ToListAsync(ct);
        foreach (var link in links.Where(link => desired.All(person => person.Id != link.PersonId)))
            unitOfWork.DataContext.VolumePeople.Remove(link);
        foreach (var person in desired.Where(person => links.All(link => link.PersonId != person.Id)))
        {
            unitOfWork.DataContext.VolumePeople.Add(new VolumePeople
            {
                VolumeId = volume.Id, Person = person, Role = PersonRole.Writer
            });
        }
    }

    /// <summary>Recover publication credits imported before the publication–person link existed.</summary>
    public static async Task BackfillAsync(IUnitOfWork unitOfWork, CancellationToken ct = default)
    {
        var missingIds = await unitOfWork.DataContext.Volume
            .Where(volume => volume.Series.Library.Type == LibraryType.Rpg &&
                             volume.RpgMaterialType >= RpgMaterialType.CoreManual &&
                             volume.RpgMaterialType <= RpgMaterialType.OtherPublication &&
                             !volume.People.Any())
            .Select(volume => volume.Id).ToListAsync(ct);
        foreach (var id in missingIds)
        {
            var volume = await unitOfWork.DataContext.Volume.FirstAsync(item => item.Id == id, ct);
            if (volume.RpgMaterialType.IsPublication() && volume.RpgWriters.Count > 0)
            {
                await SyncAsync(volume, unitOfWork, ct);
                if (!await unitOfWork.CommitAsync(ct))
                    throw new InvalidOperationException("Failed to backfill RPG publication people");
            }
            // The startup scope lives until shutdown; do not retain every scanned publication in memory.
            unitOfWork.DataContext.ChangeTracker.Clear();
        }
    }
}
