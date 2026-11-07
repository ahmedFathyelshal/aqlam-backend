using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Learning;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using AqlamAcademy.Domain.Assessments;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Learning;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Student-side learning. Course structure and progress come from the tenant store; enrollments and memberships
/// from the platform store. Access and completion rules are shared through <see cref="LearnerAccess"/>.
/// </summary>
public class LearningService : ILearningService
{
    private readonly TenantDbContext _tenant;
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentOrganization _currentOrganization;
    private readonly IMembershipService _memberships;
    private readonly LearnerAccess _access;

    public LearningService(TenantDbContext tenant, ApplicationDbContext platform, ICurrentUserService currentUser,
        ICurrentOrganization currentOrganization, IMembershipService memberships, LearnerAccess access)
    {
        _tenant = tenant;
        _platform = platform;
        _currentUser = currentUser;
        _currentOrganization = currentOrganization;
        _memberships = memberships;
        _access = access;
    }

    public async Task<Result<LearnerCourseDto>> GetCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var access = await _access.ResolveAsync(courseId, cancellationToken);
        if (access.Failure is { } failure)
            return Fail<LearnerCourseDto>(failure);

        var (course, enrollment, isPreview) = (access.Course!, access.Enrollment, access.IsPreview);
        var progress = await _access.LoadProgressAsync(course.Id, cancellationToken);
        var lockStates = _access.EvaluateLocks(course, enrollment, isPreview, progress);

        var chapters = course.Chapters
            .OrderBy(c => c.Order)
            .Select(c => new LearnerChapterDto(
                c.Id,
                c.Title,
                c.Lessons.Where(l => l.IsPublished).OrderBy(l => l.Order).Select(l =>
                {
                    var state = lockStates[l.Id];
                    progress.TryGetValue(l.Id, out var p);
                    return new LearnerLessonDto(l.Id, l.Title, l.Type.ToString(), l.DurationInMinutes,
                        state.IsLocked, state.IsLocked ? state.Reason.ToString() : null, state.UnlocksAtUtc,
                        p?.IsCompleted ?? false, p?.WatchedPercent ?? 0);
                }).ToList()))
            .Where(c => c.Lessons.Count > 0)
            .ToList();

        // Keep the enrollment's title snapshot fresh for "My Courses".
        if (enrollment is not null && enrollment.CourseTitle != course.Title)
        {
            enrollment.CourseTitle = course.Title;
            await _platform.SaveChangesAsync(cancellationToken);
        }

        return Result<LearnerCourseDto>.Success(new LearnerCourseDto(
            course.Id, course.Title, course.Subtitle, course.ThumbnailUrl, course.Status.ToString(), isPreview,
            enrollment?.ExpiresAtUtc, enrollment?.ProgressPercent ?? 0,
            enrollment?.LastLessonId ?? LessonAccess.VisibleLessons(course).FirstOrDefault()?.Id,
            chapters));
    }

    public async Task<Result<LearnerHomeDto>> GetHomeAsync(CancellationToken cancellationToken)
    {
        if (_currentOrganization.OrganizationId is not { } orgId)
            return Result<LearnerHomeDto>.NotFound("Organization not found.");

        var now = DateTime.UtcNow;
        var enrollments = (await _platform.Enrollments.AsNoTracking()
                .Where(e => e.UserId == _currentUser.UserId && e.OrganizationId == orgId && e.Status == EnrollmentStatus.Active)
                .ToListAsync(cancellationToken))
            .Where(e => e.IsUsable(now))
            .ToList();

        var nextLessons = new List<NextLessonDto>();
        var openLessons = new Dictionary<Guid, (string CourseTitle, string Title)>();
        foreach (var enrollment in enrollments)
        {
            var access = await _access.ResolveAsync(enrollment.CourseId, cancellationToken);
            if (access.Failure is not null || access.Enrollment is null)
                continue;

            var course = access.Course!;
            var progress = await _access.LoadProgressAsync(course.Id, cancellationToken);
            var locks = _access.EvaluateLocks(course, access.Enrollment, false, progress);
            var lessons = course.Chapters.OrderBy(c => c.Order)
                .SelectMany(c => c.Lessons.Where(l => l.IsPublished).OrderBy(l => l.Order).Select(l => (Chapter: c, Lesson: l)))
                .Where(x => !locks[x.Lesson.Id].IsLocked)
                .ToList();

            foreach (var (_, lesson) in lessons)
                openLessons[lesson.Id] = (course.Title, lesson.Title);

            var next = lessons.FirstOrDefault(x => !(progress.TryGetValue(x.Lesson.Id, out var p) && p.IsCompleted));
            if (next.Lesson is not null)
                nextLessons.Add(new NextLessonDto(course.Id, next.Lesson.Id, next.Lesson.Title, next.Chapter.Title, next.Lesson.Type.ToString(), next.Lesson.DurationInMinutes));
        }

        return Result<LearnerHomeDto>.Success(new LearnerHomeDto(
            nextLessons,
            await DueItemsAsync(openLessons, now, cancellationToken),
            await LatestResultAsync(enrollments, cancellationToken)));
    }

    /// <summary>Open assignments due within a week, plus overdue ones that are still accepted (for two weeks).</summary>
    private async Task<List<DueItemDto>> DueItemsAsync(Dictionary<Guid, (string CourseTitle, string Title)> openLessons, DateTime now, CancellationToken cancellationToken)
    {
        var lessonIds = openLessons.Keys.ToList();
        var weekAhead = now.AddDays(7);
        var overdueLimit = now.AddDays(-14);
        var userId = _currentUser.UserId;

        var assignments = await _tenant.Assignments.AsNoTracking()
            .Where(a => lessonIds.Contains(a.LessonId) && a.DueAtUtc != null && a.DueAtUtc <= weekAhead
                && (a.DueAtUtc >= now || (a.AcceptLateSubmissions && a.DueAtUtc >= overdueLimit)))
            .ToListAsync(cancellationToken);
        var handedIn = await _tenant.AssignmentSubmissions.AsNoTracking()
            .Where(s => s.UserId == userId && lessonIds.Contains(s.LessonId) && s.Status != SubmissionStatus.ReturnedForRevision)
            .Select(s => s.LessonId)
            .ToListAsync(cancellationToken);

        return assignments
            .Where(a => !handedIn.Contains(a.LessonId))
            .OrderBy(a => a.DueAtUtc)
            .Select(a => new DueItemDto(a.CourseId, openLessons[a.LessonId].CourseTitle, a.LessonId, openLessons[a.LessonId].Title, a.DueAtUtc!.Value, a.DueAtUtc < now))
            .ToList();
    }

    private async Task<LatestResultDto?> LatestResultAsync(List<Enrollment> enrollments, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var courseIds = enrollments.Select(e => e.CourseId).ToList();

        var quiz = await _tenant.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.SubmittedAtUtc != null && courseIds.Contains(a.CourseId))
            .OrderByDescending(a => a.SubmittedAtUtc)
            .Select(a => new { a.CourseId, a.LessonId, a.Percent, At = a.SubmittedAtUtc!.Value })
            .FirstOrDefaultAsync(cancellationToken);
        var graded = await (
                from s in _tenant.AssignmentSubmissions.AsNoTracking()
                join a in _tenant.Assignments.AsNoTracking() on s.AssignmentId equals a.Id
                where s.UserId == userId && s.Status == SubmissionStatus.Graded && s.Score != null && a.MaxPoints > 0 && courseIds.Contains(s.CourseId)
                orderby s.GradedAtUtc descending
                select new { s.CourseId, s.LessonId, Percent = s.Score!.Value * 100 / a.MaxPoints, At = s.GradedAtUtc ?? s.SubmittedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (quiz is null && graded is null)
            return null;

        var useQuiz = graded is null || (quiz is not null && quiz.At >= graded.At);
        var (courseId, lessonId, percent, at) = useQuiz
            ? (quiz!.CourseId, quiz.LessonId, quiz.Percent, quiz.At)
            : (graded!.CourseId, graded.LessonId, Math.Round(graded.Percent, 1), graded.At);
        var title = await _tenant.CourseLessons.AsNoTracking().Where(l => l.Id == lessonId).Select(l => l.Title).FirstOrDefaultAsync(cancellationToken);

        return new LatestResultDto(useQuiz ? "Quiz" : "Assignment", courseId, enrollments.First(e => e.CourseId == courseId).CourseTitle, lessonId, title ?? string.Empty, percent, at);
    }

    public async Task<Result<LessonContentDto>> GetLessonAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var courseId = await _access.CourseIdOfLessonAsync(lessonId, cancellationToken);
        if (courseId is null)
            return Result<LessonContentDto>.NotFound("Lesson not found.");

        var access = await _access.ResolveAsync(courseId.Value, cancellationToken);
        if (access.Failure is { } failure)
            return Fail<LessonContentDto>(failure);

        var (course, enrollment, isPreview) = (access.Course!, access.Enrollment, access.IsPreview);
        var visible = LessonAccess.VisibleLessons(course);
        var index = visible.ToList().FindIndex(l => l.Id == lessonId);
        if (index < 0)
            return Result<LessonContentDto>.NotFound("Lesson not found."); // unpublished lessons are invisible to students

        var progress = await _access.LoadProgressAsync(course.Id, cancellationToken);
        var state = _access.EvaluateLocks(course, enrollment, isPreview, progress)[lessonId];
        if (state.IsLocked)
            return Result<LessonContentDto>.Failure(ResultErrorType.Forbidden, LearnerAccess.DescribeLock(state));

        var lesson = visible[index];
        progress.TryGetValue(lessonId, out var p);

        return Result<LessonContentDto>.Success(new LessonContentDto(
            lesson.Id, course.Id, lesson.Title, lesson.Type.ToString(), lesson.DurationInMinutes,
            lesson.VideoUrl, lesson.Summary, lesson.Content,
            lesson.Resources.OrderBy(r => r.CreatedAtUtc).Select(r => new LessonResourceDto
            {
                Id = r.Id,
                DisplayName = r.DisplayName,
                ResourceUrl = r.ResourceUrl,
                Type = r.Type.ToString(),
                IsDownloadable = r.IsDownloadable
            }).ToList(),
            p?.WatchedPercent ?? 0, p?.LastPositionSeconds ?? 0, p?.IsCompleted ?? false,
            index > 0 ? visible[index - 1].Id : null,
            index < visible.Count - 1 ? visible[index + 1].Id : null));
    }

    public async Task<Result<ProgressResultDto>> RecordProgressAsync(Guid lessonId, int? positionSeconds, int? watchedPercent, bool markCompleted, CancellationToken cancellationToken)
    {
        var (access, lesson, failure) = await _access.ResolveUnlockedLessonForStudentAsync(lessonId, cancellationToken);
        if (failure is not null)
            return Fail<ProgressResultDto>(failure);

        if (markCompleted && lesson!.Type is LessonType.Quiz or LessonType.Assignment)
            return Result<ProgressResultDto>.Failure(new[] { "Quizzes and assignments are completed by submitting them." });
        if (markCompleted && lesson!.Type == LessonType.Video)
            return Result<ProgressResultDto>.Failure(new[] { "Videos are completed by watching them." });

        var (progress, justCompleted, percent) = await _access.RecordAsync(
            access.Course!, access.Enrollment!, lessonId,
            update: p =>
            {
                if (positionSeconds is { } position)
                    p.LastPositionSeconds = position;
                if (watchedPercent is { } watched)
                    p.WatchedPercent = Math.Max(p.WatchedPercent, watched); // never goes backwards
            },
            complete: p => lesson!.Type switch
            {
                LessonType.Video => p.WatchedPercent >= LessonProgress.VideoCompletionThreshold,
                LessonType.Article => markCompleted,
                _ => false
            },
            cancellationToken);

        return Result<ProgressResultDto>.Success(new ProgressResultDto(
            lessonId, progress.WatchedPercent, progress.LastPositionSeconds, progress.IsCompleted, justCompleted, percent));
    }

    public async Task<Result<MyCourseDto>> EnrollSelfAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (_currentOrganization.OrganizationId is not { } orgId)
            return Result<MyCourseDto>.NotFound("Organization not found.");

        var course = await _tenant.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);
        // Only published courses that are open to outsiders can be joined without the organization's staff.
        if (course is null || course.Status != CourseStatus.Published || course.Visibility == CourseVisibility.Private)
            return Result<MyCourseDto>.NotFound("Course not found.");
        if (course.PricingType != PricingType.Free || course.CodeActivationOnly)
            return Result<MyCourseDto>.Failure(ResultErrorType.Forbidden, "This course requires a purchase or an activation code.");

        var userId = _currentUser.UserId;
        var membership = await _platform.Memberships.FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == userId, cancellationToken);
        if (membership is { Status: MembershipStatus.Pending })
            return Result<MyCourseDto>.Failure(ResultErrorType.Forbidden, "Your membership is waiting for approval.");
        if (membership is { Status: MembershipStatus.Suspended })
            return Result<MyCourseDto>.Failure(ResultErrorType.Forbidden, "Your membership in this organization is suspended.");

        if (membership is null)
        {
            _platform.Memberships.Add(new Membership
            {
                OrganizationId = orgId,
                UserId = userId,
                Roles = OrganizationRole.Student,
                Status = MembershipStatus.Active,
                Source = MembershipSource.PublicEnrollment
            });
        }
        else if (membership.Status == MembershipStatus.Left)
        {
            membership.Roles = OrganizationRole.Student;
            membership.Status = MembershipStatus.Active;
            membership.Source = MembershipSource.PublicEnrollment;
            membership.JoinedAtUtc = DateTime.UtcNow;
        }
        else
        {
            membership.Roles |= OrganizationRole.Student;
        }

        var enrollment = await _platform.Enrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId, cancellationToken);
        if (enrollment is not null && enrollment.IsUsable(DateTime.UtcNow))
            return Result<MyCourseDto>.Conflict("You are already enrolled in this course.");

        UpsertEnrollment(enrollment, userId, orgId, course, EnrollmentSource.FreeEnrollment);
        await _platform.SaveChangesAsync(cancellationToken);
        _memberships.Invalidate(userId, orgId);

        return Result<MyCourseDto>.Success((await GetMyCoursesAsync(cancellationToken)).First(c => c.CourseId == courseId));
    }

    public async Task<Result<IReadOnlyList<EnrollMemberResultDto>>> EnrollMembersAsync(Guid courseId, IReadOnlyList<string> userNames, CancellationToken cancellationToken)
    {
        if (_currentOrganization.OrganizationId is not { } orgId)
            return Result<IReadOnlyList<EnrollMemberResultDto>>.NotFound("Organization not found.");

        var course = await _tenant.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);
        if (course is null)
            return Result<IReadOnlyList<EnrollMemberResultDto>>.NotFound("Course not found.");
        if (course.Status is not (CourseStatus.Published or CourseStatus.Scheduled))
            return Result<IReadOnlyList<EnrollMemberResultDto>>.Conflict("Students can only be enrolled in scheduled or published courses.");

        var normalized = userNames.Select(u => u.Trim()).Where(u => u.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var upper = normalized.Select(u => u.ToUpperInvariant()).ToList();

        var members = await (
                from u in _platform.Users
                join m in _platform.Memberships on u.Id equals m.UserId
                where upper.Contains(u.NormalizedUserName!) && m.OrganizationId == orgId
                select new { User = u, Membership = m })
            .ToListAsync(cancellationToken);
        var userIds = members.Select(m => m.User.Id).ToList();
        var existing = await _platform.Enrollments.Where(e => e.CourseId == courseId && userIds.Contains(e.UserId)).ToDictionaryAsync(e => e.UserId, cancellationToken);

        var now = DateTime.UtcNow;
        var results = new List<EnrollMemberResultDto>();
        foreach (var userName in normalized)
        {
            var member = members.FirstOrDefault(m => string.Equals(m.User.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (member is null || member.Membership.Status != MembershipStatus.Active)
            {
                results.Add(new EnrollMemberResultDto(userName, "Failed", "Not an active member of this organization."));
                continue;
            }

            existing.TryGetValue(member.User.Id, out var enrollment);
            if (enrollment is not null && enrollment.IsUsable(now))
            {
                results.Add(new EnrollMemberResultDto(userName, "AlreadyEnrolled", null));
                continue;
            }

            member.Membership.Roles |= OrganizationRole.Student;
            UpsertEnrollment(enrollment, member.User.Id, orgId, course, EnrollmentSource.Manual);
            results.Add(new EnrollMemberResultDto(userName, "Enrolled", null));
        }

        await _platform.SaveChangesAsync(cancellationToken);
        foreach (var userId in userIds)
            _memberships.Invalidate(userId, orgId);

        return Result<IReadOnlyList<EnrollMemberResultDto>>.Success(results);
    }

    public async Task<Result<IReadOnlyList<EnrollmentDto>>> ListEnrollmentsAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (_currentOrganization.OrganizationId is not { } orgId)
            return Result<IReadOnlyList<EnrollmentDto>>.NotFound("Organization not found.");
        if (!await _tenant.Courses.AnyAsync(c => c.Id == courseId, cancellationToken))
            return Result<IReadOnlyList<EnrollmentDto>>.NotFound("Course not found.");

        var rows = await (
                from e in _platform.Enrollments.AsNoTracking()
                join u in _platform.Users.AsNoTracking() on e.UserId equals u.Id
                where e.OrganizationId == orgId && e.CourseId == courseId && e.Status == EnrollmentStatus.Active
                orderby u.FullName
                select new EnrollmentDto(e.UserId, u.UserName!, u.FullName, e.Source.ToString(), e.StartsAtUtc, e.ExpiresAtUtc, e.ProgressPercent))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EnrollmentDto>>.Success(rows);
    }

    public async Task<IReadOnlyList<MyCourseDto>> GetMyCoursesAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await (
                from e in _platform.Enrollments.AsNoTracking()
                join o in _platform.Organizations.AsNoTracking() on e.OrganizationId equals o.Id
                where e.UserId == _currentUser.UserId && e.Status == EnrollmentStatus.Active && o.Status == OrganizationStatus.Active
                orderby e.LastAccessedAtUtc ?? e.StartsAtUtc descending
                select new { e, o.Slug, o.Name })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new MyCourseDto(
                r.e.CourseId, r.e.CourseTitle, r.e.OrganizationId, r.Slug, r.Name, r.e.ProgressPercent,
                r.e.LastLessonId, r.e.LastAccessedAtUtc, r.e.ExpiresAtUtc, !r.e.IsUsable(now)))
            .ToList();
    }

    private void UpsertEnrollment(Enrollment? enrollment, Guid userId, Guid orgId, Course course, EnrollmentSource source) =>
        EnrollmentWriter.Upsert(_platform, enrollment, userId, orgId, course, source);

    private static Result<T> Fail<T>(Result failure) =>
        Result<T>.Failure(failure.ErrorType, failure.Errors);
}
