namespace AqlamAcademy.Application.Common.Interfaces;

/// <summary>
/// Who may create and work on courses in the current organization.
/// In a school the school owns subjects and their courses (grades and report cards hang off them): owners, admins
/// and coordinators create them through the classes, and a teacher works only on the courses of the subjects they
/// teach. A center works the same way unless it lets teachers create their own courses. In a personal academy the
/// teacher owns everything.
/// </summary>
public interface ICourseAccess
{
    /// <summary>Owners, admins and coordinators (and anyone in a personal academy) see every course.</summary>
    Task<bool> SeesAllCoursesAsync(CancellationToken cancellationToken);

    Task<bool> CanCreateAsync(CancellationToken cancellationToken);

    /// <summary>The course is one the user may edit: they see all courses, created it, or teach its subject.</summary>
    Task<bool> CanWorkOnAsync(Guid courseId, CancellationToken cancellationToken);
}
