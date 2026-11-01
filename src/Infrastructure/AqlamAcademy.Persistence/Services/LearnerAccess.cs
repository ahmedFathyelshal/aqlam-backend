using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Learning;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Shared student-side rules used by the player and by assessments: who may open a course, which lessons
/// are locked, and how progress/completion is recorded (including the enrollment snapshot).
/// </summary>
public class LearnerAccess
{
    private readonly TenantDbContext _tenant;
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentOrganization _currentOrganization;
    private readonly IMembershipService _memberships;

    public LearnerAccess(TenantDbContext tenant, ApplicationDbContext platform, ICurrentUserService currentUser,
        ICurrentOrganization currentOrganization, IMembershipService memberships)
    {
        _tenant = tenant;
        _platform = platform;
        _currentUser = currentUser;
        _currentOrganization = currentOrganization;
        _memberships = memberships;
    }

    public Guid UserId => _currentUser.UserId;

    public sealed record AccessResult(Course? Course, Enrollment? Enrollment, bool IsPreview, Result? Failure);

    /// <summary>
    /// Staff (courses.view) may preview any course of their organization. Students need an active, unexpired
    /// enrollment and never see drafts or suspended courses. Outsiders cannot even confirm a course exists.
    /// </summary>
    public async Task<AccessResult> ResolveAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (_currentOrganization.OrganizationId is not { } orgId)
            return new(null, null, false, Result<bool>.NotFound("Organization not found."));

        var course = await _tenant.Courses
            .Include(c => c.Chapters).ThenInclude(ch => ch.Lessons).ThenInclude(l => l.Resources)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);
        if (course is null)
            return new(null, null, false, Result<bool>.NotFound("Course not found."));

        var roles = await _memberships.GetActiveRolesAsync(UserId, orgId, cancellationToken);
        if (Permissions.Grants(roles, Permissions.CoursesView))
            return new(course, null, true, null);

        if (course.Status is CourseStatus.Draft or CourseStatus.Suspended)
            return new(null, null, false, Result<bool>.NotFound("Course not found."));

        var enrollment = await _platform.Enrollments
            .FirstOrDefaultAsync(e => e.UserId == UserId && e.CourseId == courseId && e.OrganizationId == orgId, cancellationToken);

        if ((enrollment is null || enrollment.Status != EnrollmentStatus.Active) && roles == OrganizationRole.None)
            return new(null, null, false, Result<bool>.NotFound("Course not found."));
        if (enrollment is null || enrollment.Status != EnrollmentStatus.Active)
            return new(null, null, false, Result<bool>.Failure(ResultErrorType.Forbidden, "You are not enrolled in this course."));
        if (!enrollment.IsUsable(DateTime.UtcNow))
            return new(null, null, false, Result<bool>.Failure(ResultErrorType.Forbidden, "Your access to this course has expired."));

        return new(course, enrollment, false, null);
    }

    public IReadOnlyDictionary<Guid, LessonAccessState> EvaluateLocks(
        Course course, Enrollment? enrollment, bool isPreview, IReadOnlyDictionary<Guid, LessonProgress> progress)
    {
        if (isPreview)
            return LessonAccess.VisibleLessons(course).ToDictionary(l => l.Id, l => new LessonAccessState(l.Id, false, LockReason.None, null));

        var completed = progress.Values.Where(p => p.IsCompleted).Select(p => p.LessonId).ToHashSet();
        return LessonAccess.Evaluate(course, enrollment!.StartsAtUtc, completed, DateTime.UtcNow);
    }

    public async Task<Dictionary<Guid, LessonProgress>> LoadProgressAsync(Guid courseId, CancellationToken cancellationToken) =>
        await _tenant.LessonProgress.AsNoTracking()
            .Where(p => p.UserId == UserId && p.CourseId == courseId)
            .ToDictionaryAsync(p => p.LessonId, cancellationToken);

    public Task<Guid?> CourseIdOfLessonAsync(Guid lessonId, CancellationToken cancellationToken) =>
        _tenant.CourseLessons.AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => (Guid?)l.Chapter.CourseId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Resolves access to a lesson for an enrolled student and checks it is visible and unlocked.
    /// Returns a failure for previews: assessments and progress are only recorded for enrolled students.
    /// </summary>
    public async Task<(AccessResult Access, CourseLesson? Lesson, Result? Failure)> ResolveUnlockedLessonForStudentAsync(
        Guid lessonId, CancellationToken cancellationToken)
    {
        var courseId = await CourseIdOfLessonAsync(lessonId, cancellationToken);
        if (courseId is null)
            return (new(null, null, false, null), null, Result<bool>.NotFound("Lesson not found."));

        var access = await ResolveAsync(courseId.Value, cancellationToken);
        if (access.Failure is not null)
            return (access, null, access.Failure);
        if (access.Enrollment is null)
            return (access, null, Result<bool>.Failure(ResultErrorType.Forbidden, "Only enrolled students can do this (preview mode)."));

        var lesson = LessonAccess.VisibleLessons(access.Course!).FirstOrDefault(l => l.Id == lessonId);
        if (lesson is null)
            return (access, null, Result<bool>.NotFound("Lesson not found."));

        var state = EvaluateLocks(access.Course!, access.Enrollment, false, await LoadProgressAsync(access.Course!.Id, cancellationToken))[lessonId];
        if (state.IsLocked)
            return (access, null, Result<bool>.Failure(ResultErrorType.Forbidden, DescribeLock(state)));

        return (access, lesson, null);
    }

    /// <summary>
    /// Applies an update to the student's progress on a lesson, completes it when <paramref name="complete"/>
    /// is true (only the first time), and refreshes the enrollment snapshot. Saves both stores.
    /// </summary>
    public async Task<(LessonProgress Progress, bool JustCompleted, int CoursePercent)> RecordAsync(
        Course course, Enrollment enrollment, Guid lessonId, Action<LessonProgress> update,
        Func<LessonProgress, bool> complete, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var progress = await _tenant.LessonProgress.FirstOrDefaultAsync(p => p.UserId == UserId && p.LessonId == lessonId, cancellationToken);
        if (progress is null)
        {
            progress = new LessonProgress { UserId = UserId, CourseId = course.Id, LessonId = lessonId };
            _tenant.LessonProgress.Add(progress);
        }

        update(progress);
        progress.LastModifiedAtUtc = now;

        var justCompleted = false;
        if (!progress.IsCompleted && complete(progress))
        {
            progress.CompletedAtUtc = now; // the "lesson completed" moment, set once
            justCompleted = true;
        }

        await _tenant.SaveChangesAsync(cancellationToken);

        var visible = LessonAccess.VisibleLessons(course);
        var completedIds = (await LoadProgressAsync(course.Id, cancellationToken)).Values
            .Where(p => p.IsCompleted).Select(p => p.LessonId).ToHashSet();
        enrollment.ProgressPercent = visible.Count == 0 ? 0 : (int)Math.Round(100.0 * visible.Count(l => completedIds.Contains(l.Id)) / visible.Count);
        enrollment.LastLessonId = lessonId;
        enrollment.LastAccessedAtUtc = now;
        await _platform.SaveChangesAsync(cancellationToken);

        return (progress, justCompleted, enrollment.ProgressPercent);
    }

    public static string DescribeLock(LessonAccessState state) => state.Reason switch
    {
        LockReason.CourseNotStarted => "This course has not started yet.",
        LockReason.DripSchedule => $"This lesson unlocks on {state.UnlocksAtUtc:yyyy-MM-dd}.",
        LockReason.PreviousLessonRequired => "Complete the previous lesson first.",
        _ => "This lesson is locked."
    };
}
