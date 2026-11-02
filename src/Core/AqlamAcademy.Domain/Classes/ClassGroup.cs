using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Classes;

/// <summary>Who a branch or class is for. Saudi schools separate boys and girls; centers are often mixed.</summary>
public enum GenderSegment
{
    Mixed = 1,
    Boys = 2,
    Girls = 3
}

public enum Gender
{
    Male = 1,
    Female = 2
}

/// <summary>School: a class/section (e.g. Grade 7-B). Center: a group with a weekly schedule.</summary>
public enum ClassKind
{
    Section = 1,
    Group = 2
}

/// <summary>A location inside an organization: a center's branch or a school's campus (e.g. the girls' campus).</summary>
public class Branch : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public GenderSegment Gender { get; set; } = GenderSegment.Mixed;
    public string? Address { get; set; }
}

/// <summary>One weekly meeting of a center group (times are in the organization's time zone).</summary>
public class WeeklySlot
{
    public DayOfWeek Day { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}

/// <summary>
/// A class (school) or group (center). Students belong to many classes; each subject taught in a class is a
/// <see cref="TeachingAssignment"/> with its own course ("subject space").
/// </summary>
public class ClassGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public ClassKind Kind { get; set; }
    public GenderSegment Gender { get; set; } = GenderSegment.Mixed;

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    // Platform lookup ids (no cross-store foreign keys)
    public int? CurriculumId { get; set; }
    public int? GradeId { get; set; }

    public Guid? HomeroomTeacherUserId { get; set; }
    /// <summary>Null = no limit.</summary>
    public int? Capacity { get; set; }

    public List<WeeklySlot> Schedule { get; set; } = new();

    public ICollection<ClassStudent> Students { get; set; } = new List<ClassStudent>();
    public ICollection<TeachingAssignment> Assignments { get; set; } = new List<TeachingAssignment>();

    /// <summary>A student of the given gender may join; unknown gender is allowed (the school fixes data later).</summary>
    public bool Accepts(Gender? gender) => Gender switch
    {
        GenderSegment.Boys => gender != Classes.Gender.Female,
        GenderSegment.Girls => gender != Classes.Gender.Male,
        _ => true
    };
}

/// <summary>A student on a class roster. Removing a student deletes the row (history lives in attendance and grades).</summary>
public class ClassStudent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClassGroupId { get; set; }
    public Guid UserId { get; set; }
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Teacher ↔ subject ↔ class. Creating one creates the class's course for that subject.</summary>
public class TeachingAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClassGroupId { get; set; }

    /// <summary>Platform subject lookup, or null for a custom subject (name only).</summary>
    public int? SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;

    public Guid TeacherUserId { get; set; }
    public Guid CourseId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
