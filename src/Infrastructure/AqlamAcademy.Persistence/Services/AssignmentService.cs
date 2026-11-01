using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Assessments;
using AqlamAcademy.Domain.Assessments;
using AqlamAcademy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Assignment settings, student submissions (which complete the lesson) and the staff grading queue.
/// Settings default to 10 points, no due date, when a teacher has not configured them.
/// </summary>
public class AssignmentService : IAssignmentService
{
    private readonly TenantDbContext _tenant;
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentUserService _currentUser;
    private readonly LearnerAccess _access;

    public AssignmentService(TenantDbContext tenant, ApplicationDbContext platform, ICurrentUserService currentUser, LearnerAccess access)
    {
        _tenant = tenant;
        _platform = platform;
        _currentUser = currentUser;
        _access = access;
    }

    public async Task<Result<AssignmentSettingsDto>> GetSettingsAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken)
    {
        if (await CheckAssignmentLessonAsync(courseId, lessonId, cancellationToken) is { } failure)
            return Result<AssignmentSettingsDto>.Failure(failure.ErrorType, failure.Errors);

        var assignment = await _tenant.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.LessonId == lessonId, cancellationToken);
        return Result<AssignmentSettingsDto>.Success(ToDto(assignment ?? new Assignment { LessonId = lessonId }));
    }

    public async Task<Result<AssignmentSettingsDto>> SaveSettingsAsync(Guid courseId, Guid lessonId, decimal maxPoints, DateTime? dueAtUtc, bool acceptLate, CancellationToken cancellationToken)
    {
        if (await CheckAssignmentLessonAsync(courseId, lessonId, cancellationToken) is { } failure)
            return Result<AssignmentSettingsDto>.Failure(failure.ErrorType, failure.Errors);

        var assignment = await GetOrCreateAsync(courseId, lessonId, cancellationToken);
        assignment.MaxPoints = maxPoints;
        assignment.DueAtUtc = dueAtUtc;
        assignment.AcceptLateSubmissions = acceptLate;
        assignment.LastModifiedAtUtc = DateTime.UtcNow;
        await _tenant.SaveChangesAsync(cancellationToken);

        return Result<AssignmentSettingsDto>.Success(ToDto(assignment));
    }

    public async Task<Result<IReadOnlyList<SubmissionDto>>> ListSubmissionsAsync(Guid courseId, bool pendingOnly, CancellationToken cancellationToken)
    {
        if (!await _tenant.Courses.AnyAsync(c => c.Id == courseId, cancellationToken))
            return Result<IReadOnlyList<SubmissionDto>>.NotFound("Course not found.");

        var query = _tenant.AssignmentSubmissions.AsNoTracking().Where(s => s.CourseId == courseId);
        if (pendingOnly)
            query = query.Where(s => s.Status == SubmissionStatus.Submitted);

        var submissions = await query.OrderBy(s => s.SubmittedAtUtc).ToListAsync(cancellationToken);
        var lessonIds = submissions.Select(s => s.LessonId).Distinct().ToList();
        var lessons = await _tenant.CourseLessons.AsNoTracking().Where(l => lessonIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Title, cancellationToken);
        var maxPoints = await _tenant.Assignments.AsNoTracking().Where(a => lessonIds.Contains(a.LessonId)).ToDictionaryAsync(a => a.LessonId, a => a.MaxPoints, cancellationToken);

        // Student names live in the platform store.
        var userIds = submissions.Select(s => s.UserId).Distinct().ToList();
        var students = await _platform.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.UserName, u.FullName }, cancellationToken);

        return Result<IReadOnlyList<SubmissionDto>>.Success(submissions.Select(s => ToDto(
            s,
            students.TryGetValue(s.UserId, out var student) ? student.UserName : null,
            student?.FullName,
            lessons.GetValueOrDefault(s.LessonId),
            maxPoints.GetValueOrDefault(s.LessonId, 10))).ToList());
    }

    public async Task<Result<SubmissionDto>> GradeAsync(Guid submissionId, decimal? score, string? feedback, bool returnForRevision, CancellationToken cancellationToken)
    {
        var submission = await _tenant.AssignmentSubmissions.FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);
        if (submission is null)
            return Result<SubmissionDto>.NotFound("Submission not found.");

        var maxPoints = (await _tenant.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.LessonId == submission.LessonId, cancellationToken))?.MaxPoints ?? 10;
        if (!returnForRevision && score > maxPoints)
            return Result<SubmissionDto>.Failure(new[] { $"The score cannot be higher than {maxPoints}." });

        submission.Status = returnForRevision ? SubmissionStatus.ReturnedForRevision : SubmissionStatus.Graded;
        submission.Score = returnForRevision ? null : score;
        submission.Feedback = feedback;
        submission.GradedByUserId = _currentUser.UserId;
        submission.GradedAtUtc = DateTime.UtcNow;
        await _tenant.SaveChangesAsync(cancellationToken);

        return Result<SubmissionDto>.Success(ToDto(submission, null, null, null, maxPoints));
    }

    public async Task<Result<StudentAssignmentDto>> GetForStudentAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var (_, lesson, failure) = await _access.ResolveUnlockedLessonForStudentAsync(lessonId, cancellationToken);
        if (failure is not null)
            return Result<StudentAssignmentDto>.Failure(failure.ErrorType, failure.Errors);
        if (lesson!.Type != LessonType.Assignment)
            return Result<StudentAssignmentDto>.NotFound("This lesson is not an assignment.");

        var assignment = await _tenant.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.LessonId == lessonId, cancellationToken)
                         ?? new Assignment { LessonId = lessonId };
        var submission = await _tenant.AssignmentSubmissions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LessonId == lessonId && s.UserId == _currentUser.UserId, cancellationToken);

        return Result<StudentAssignmentDto>.Success(new StudentAssignmentDto(
            lessonId, assignment.MaxPoints, assignment.DueAtUtc, assignment.AcceptLateSubmissions,
            CanSubmit(assignment, submission, DateTime.UtcNow),
            submission is null ? null : ToDto(submission, null, null, lesson.Title, assignment.MaxPoints)));
    }

    public async Task<Result<SubmissionDto>> SubmitAsync(Guid lessonId, string? text, IReadOnlyList<string> links, CancellationToken cancellationToken)
    {
        var (access, lesson, failure) = await _access.ResolveUnlockedLessonForStudentAsync(lessonId, cancellationToken);
        if (failure is not null)
            return Result<SubmissionDto>.Failure(failure.ErrorType, failure.Errors);
        if (lesson!.Type != LessonType.Assignment)
            return Result<SubmissionDto>.NotFound("This lesson is not an assignment.");

        var now = DateTime.UtcNow;
        var assignment = await GetOrCreateAsync(access.Course!.Id, lessonId, cancellationToken);
        var submission = await _tenant.AssignmentSubmissions.FirstOrDefaultAsync(s => s.LessonId == lessonId && s.UserId == _currentUser.UserId, cancellationToken);

        if (submission is { CanBeChangedByStudent: false })
            return Result<SubmissionDto>.Conflict("This assignment has already been graded.");
        if (!CanSubmit(assignment, submission, now))
            return Result<SubmissionDto>.Failure(ResultErrorType.Forbidden, "The due date has passed and late submissions are not accepted.");

        if (submission is null)
        {
            submission = new AssignmentSubmission { AssignmentId = assignment.Id, CourseId = access.Course.Id, LessonId = lessonId, UserId = _currentUser.UserId };
            _tenant.AssignmentSubmissions.Add(submission);
        }

        submission.Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        submission.Links = links.Select(l => l.Trim()).ToList();
        submission.SubmittedAtUtc = now;
        submission.IsLate = assignment.DueAtUtc is { } due && now > due;
        submission.Status = SubmissionStatus.Submitted; // a revision goes back into the grading queue
        submission.LastModifiedAtUtc = now;
        await _tenant.SaveChangesAsync(cancellationToken);

        // Handing in the work completes the lesson (the grade does not block progress).
        await _access.RecordAsync(access.Course, access.Enrollment!, lessonId, update: _ => { }, complete: _ => true, cancellationToken);

        return Result<SubmissionDto>.Success(ToDto(submission, null, null, lesson.Title, assignment.MaxPoints));
    }

    private static bool CanSubmit(Assignment assignment, AssignmentSubmission? submission, DateTime now) =>
        (submission is null || submission.CanBeChangedByStudent)
        && (assignment.DueAtUtc is null || now <= assignment.DueAtUtc || assignment.AcceptLateSubmissions);

    private async Task<Assignment> GetOrCreateAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken)
    {
        var assignment = await _tenant.Assignments.FirstOrDefaultAsync(a => a.LessonId == lessonId, cancellationToken);
        if (assignment is null)
        {
            assignment = new Assignment { CourseId = courseId, LessonId = lessonId };
            _tenant.Assignments.Add(assignment);
        }
        return assignment;
    }

    private async Task<Result?> CheckAssignmentLessonAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken)
    {
        var type = await _tenant.CourseLessons.AsNoTracking()
            .Where(l => l.Id == lessonId && l.Chapter.CourseId == courseId)
            .Select(l => (LessonType?)l.Type)
            .FirstOrDefaultAsync(cancellationToken);

        if (type is null)
            return Result<bool>.NotFound("Lesson not found in this course.");
        return type == LessonType.Assignment ? null : Result<bool>.Conflict("This lesson is not an assignment. Change its type to Assignment first.");
    }

    private static AssignmentSettingsDto ToDto(Assignment a) => new(a.LessonId, a.MaxPoints, a.DueAtUtc, a.AcceptLateSubmissions);

    private static SubmissionDto ToDto(AssignmentSubmission s, string? userName, string? fullName, string? lessonTitle, decimal maxPoints) => new(
        s.Id, s.LessonId, s.UserId, userName, fullName, lessonTitle, s.Text, s.Links, s.SubmittedAtUtc, s.IsLate,
        s.Status.ToString(), s.Score, maxPoints, s.Feedback, s.GradedAtUtc);
}
