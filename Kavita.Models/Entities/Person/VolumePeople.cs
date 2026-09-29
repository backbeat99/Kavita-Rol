using Kavita.Models.Entities.Enums;

namespace Kavita.Models.Entities.Person;

/// <summary>A person's credit on an RPG publication, independent of its versions and game.</summary>
public class VolumePeople
{
    public int VolumeId { get; set; }
    public Volume Volume { get; set; } = null!;
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public PersonRole Role { get; set; }
}
