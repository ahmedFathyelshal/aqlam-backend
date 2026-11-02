using AqlamAcademy.Domain.Classes;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Learning;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>Creates or reactivates a student's enrollment (one row per student and course).</summary>
public static class EnrollmentWriter
{
    public static Enrollment Upsert(ApplicationDbContext platform, Enrollment? enrollment, Guid userId, Guid orgId, Course course, EnrollmentSource source)
    {
        var now = DateTime.UtcNow;
        if (enrollment is null)
        {
            enrollment = new Enrollment { UserId = userId, OrganizationId = orgId, CourseId = course.Id };
            platform.Enrollments.Add(enrollment);
        }

        // New or renewed access starts now (expired/revoked enrollments are reused).
        enrollment.CourseTitle = course.Title;
        enrollment.Source = source;
        enrollment.Status = EnrollmentStatus.Active;
        enrollment.StartsAtUtc = now;
        enrollment.ExpiresAtUtc = course.AccessExpiryPolicy == AccessExpiryPolicy.Limited && course.AccessDurationDays is > 0
            ? now.AddDays(course.AccessDurationDays.Value)
            : null;
        return enrollment;
    }
}

/// <summary>
/// Keeps class rosters and subject-space enrollments in step: students of a class are enrolled in every
/// scheduled/published course taught in that class, and lose class-given access when they leave it.
/// Enrollments from other sources (purchase, code, manual) are never touched.
/// </summary>
public class ClassEnrollmentSync
{
    private readonly TenantDbContext _tenant;
    private readonly ApplicationDbContext _platform;

    public ClassEnrollmentSync(TenantDbContext tenant, ApplicationDbContext platform)
    {
        _tenant = tenant;
        _platform = platform;
    }

    private static bool IsOpen(Course course) => course.Status is CourseStatus.Published or CourseStatus.Scheduled;

    /// <summary>After a course goes live: enroll every student of every class that studies it.</summary>
    public async Task EnrollRostersOfCourseAsync(Guid orgId, Course course, CancellationToken cancellationToken)
    {
        if (!IsOpen(course))
            return;

        var studentIds = await (
                from a in _tenant.TeachingAssignments
                join s in _tenant.ClassStudents on a.ClassGroupId equals s.ClassGroupId
                where a.CourseId == course.Id
                select s.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        await EnrollAsync(orgId, [course], studentIds, cancellationToken);
    }

    /// <summary>After students join a class: enroll them in the class's live courses.</summary>
    public async Task EnrollStudentsInClassCoursesAsync(Guid orgId, Guid classId, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        var courseIds = await _tenant.TeachingAssignments.Where(a => a.ClassGroupId == classId).Select(a => a.CourseId).ToListAsync(cancellationToken);
        var courses = await _tenant.Courses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToListAsync(cancellationToken);
        await EnrollAsync(orgId, courses.Where(IsOpen).ToList(), studentIds, cancellationToken);
    }

    /// <summary>
    /// After students leave a class or a subject is removed from it: revoke class-given enrollments in the
    /// affected courses, unless another class still gives the student that course.
    /// </summary>
    public async Task RevokeAsync(Guid orgId, IReadOnlyCollection<Guid> courseIds, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        if (courseIds.Count == 0 || studentIds.Count == 0)
            return;

        var stillGiven = (await (
                from a in _tenant.TeachingAssignments
                join s in _tenant.ClassStudents on a.ClassGroupId equals s.ClassGroupId
                where courseIds.Contains(a.CourseId) && studentIds.Contains(s.UserId)
                select new { a.CourseId, s.UserId })
            .ToListAsync(cancellationToken))
            .Select(x => (x.CourseId, x.UserId))
            .ToHashSet();

        var enrollments = await _platform.Enrollments
            .Where(e => e.OrganizationId == orgId && courseIds.Contains(e.CourseId) && studentIds.Contains(e.UserId)
                        && e.Source == EnrollmentSource.ClassRoster && e.Status == EnrollmentStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var enrollment in enrollments.Where(e => !stillGiven.Contains((e.CourseId, e.UserId))))
            enrollment.Status = EnrollmentStatus.Revoked;

        await _platform.SaveChangesAsync(cancellationToken);
    }

    private async Task EnrollAsync(Guid orgId, IReadOnlyList<Course> courses, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        if (courses.Count == 0 || studentIds.Count == 0)
            return;

        var courseIds = courses.Select(c => c.Id).ToList();
        var existing = await _platform.Enrollments
            .Where(e => courseIds.Contains(e.CourseId) && studentIds.Contains(e.UserId))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var course in courses)
        foreach (var studentId in studentIds)
        {
            var enrollment = existing.FirstOrDefault(e => e.CourseId == course.Id && e.UserId == studentId);
            if (enrollment is not null && enrollment.IsUsable(now))
                continue; // keep a paid/code/manual enrollment as it is

            EnrollmentWriter.Upsert(_platform, enrollment, studentId, orgId, course, EnrollmentSource.ClassRoster);
        }

        await _platform.SaveChangesAsync(cancellationToken);
    }
}
