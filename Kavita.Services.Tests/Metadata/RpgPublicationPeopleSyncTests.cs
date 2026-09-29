using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Kavita.Database;
using Kavita.Models.AutoMapper;
using Kavita.Models.Builders;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Entities.Person;
using Kavita.Services.Builders;
using Kavita.Services.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kavita.Services.Tests.Metadata;

public class RpgPublicationPeopleSyncTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private DataContext _context = null!;
    private UnitOfWork _unitOfWork = null!;
    private int _seriesId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _context = new DataContext(new DbContextOptionsBuilder<DataContext>().UseSqlite(_connection).Options);
        await _context.Database.EnsureCreatedAsync();
        var library = new LibraryBuilder("RPG").Build();
        library.Type = LibraryType.Rpg;
        _context.Library.Add(library);
        await _context.SaveChangesAsync();
        var series = new SeriesBuilder("Test RPG").WithLibraryId(library.Id).Build();
        _context.Series.Add(series);
        await _context.SaveChangesAsync();
        _seriesId = series.Id;
        var mapper = new MapperConfiguration(config => config.AddMaps(typeof(AutoMapperProfiles).Assembly)).CreateMapper();
        _unitOfWork = new UnitOfWork(_context, mapper, null!);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Volume> Publication(string name, params string[] writers)
    {
        var volume = new VolumeBuilder(name).WithSeriesId(_seriesId).Build();
        volume.RpgMaterialType = RpgMaterialType.Adventure;
        volume.RpgWriters = writers;
        _context.Volume.Add(volume);
        await _context.SaveChangesAsync();
        return volume;
    }

    [Fact]
    public async Task Creates_publication_only_writer_credits_reusing_people_and_aliases_without_duplicates()
    {
        var existing = new PersonBuilder("Alice").WithAlias("A. Writer").Build();
        _context.Person.Add(existing);
        var volume = await Publication("First adventure", " A. Writer ", "a. writer", "Bob");
        var other = await Publication("Second adventure");
        var chapter = new ChapterBuilder("0").Build();
        volume.Chapters.Add(chapter);
        await _context.SaveChangesAsync();

        await RpgPublicationPeopleSync.SyncAsync(volume, _unitOfWork);
        await _unitOfWork.CommitAsync();
        await RpgPublicationPeopleSync.SyncAsync(volume, _unitOfWork);
        await _unitOfWork.CommitAsync();
        _context.ChangeTracker.Clear();

        var credits = await _context.VolumePeople.Include(link => link.Person).ToListAsync();
        Assert.Equal(2, credits.Count);
        Assert.Equal(volume.Id, credits[0].VolumeId);
        Assert.All(credits, link => Assert.Equal(PersonRole.Writer, link.Role));
        Assert.Contains(credits, link => link.PersonId == existing.Id);
        Assert.Contains(credits, link => link.Person.Name == "Bob");
        Assert.Equal(2, await _context.Person.CountAsync());
        Assert.Empty(await _context.ChapterPeople.ToListAsync());
        Assert.Empty(await _context.SeriesMetadataPeople.ToListAsync());
        Assert.DoesNotContain(credits, link => link.VolumeId == other.Id);
    }

    [Fact]
    public async Task Updates_and_removes_credits_only_for_the_edited_publication()
    {
        var first = await Publication("First", "Alice", "Bob");
        var second = await Publication("Second", "Alice");
        foreach (var volume in new[] {first, second})
            await RpgPublicationPeopleSync.SyncAsync(volume, _unitOfWork);
        await _unitOfWork.CommitAsync();

        first.RpgWriters = ["Bob", "Carol"];
        await RpgPublicationPeopleSync.SyncAsync(first, _unitOfWork);
        await _unitOfWork.CommitAsync();
        _context.ChangeTracker.Clear();

        var credits = await _context.VolumePeople.Include(link => link.Person).ToListAsync();
        Assert.Equal(new[] {"Bob", "Carol"}, credits.Where(link => link.VolumeId == first.Id)
            .Select(link => link.Person.Name).OrderBy(name => name).ToArray());
        Assert.Equal(new[] {"Alice"}, credits.Where(link => link.VolumeId == second.Id)
            .Select(link => link.Person.Name).ToArray());

        first.RpgMaterialType = RpgMaterialType.Map;
        await RpgPublicationPeopleSync.SyncAsync(first, _unitOfWork);
        await _unitOfWork.CommitAsync();
        Assert.False(await _context.VolumePeople.AnyAsync(link => link.VolumeId == first.Id));
        Assert.True(await _context.VolumePeople.AnyAsync(link => link.VolumeId == second.Id));
    }

    [Fact]
    public async Task Backfills_old_publications_but_not_resources_or_other_materials()
    {
        var publication = await Publication("Old linked publication", "Alice");
        var resource = await Publication("Map", "Resource artist");
        resource.RpgMaterialType = RpgMaterialType.Map;
        await _context.SaveChangesAsync();

        await RpgPublicationPeopleSync.BackfillAsync(_unitOfWork);
        await RpgPublicationPeopleSync.BackfillAsync(_unitOfWork);

        var credits = await _context.VolumePeople.ToListAsync();
        Assert.Single(credits);
        Assert.Equal(publication.Id, credits[0].VolumeId);
    }
}
