using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;

namespace AqlamAcademy.Application.Features.Learning;

public record LearnerLessonDto(
    Guid Id,
    string Title,
    string Type,
    int? DurationInMinutes,
    bool IsLocked,
    string? LockReason,
    DateTime? UnlocksAtUtc,
    bool IsCompleted,
    int WatchedPercent);

public record LearnerChapterDto(Guid Id, string Title, IReadOnlyList<LearnerLessonDto> Lessons);

/// <summary>Course outline as the student sees it. IsPreview = staff viewing without an enrollment.</summary>
public record LearnerCourseDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string? ThumbnailUrl,
    string Status,
    bool IsPreview,
    DateTime? AccessExpiresAtUtc,
    int ProgressPercent,
    Guid? ResumeLessonId,
    IReadOnlyList<LearnerChapterDto> Chapters);

public record LessonContentDto(
    Guid Id,
    Guid CourseId,
    string Title,
    string Type,
    int? DurationInMinutes,
    string? VideoUrl,
    string? Summary,
    string? Content,
    IReadOnlyList<LessonResourceDto> Resources,
    int WatchedPercent,
    int LastPositionSeconds,
    bool IsCompleted,
    Guid? PreviousLessonId,
    Guid? NextLessonId);

public record ProgressResultDto(
    Guid LessonId,
    int WatchedPercent,
    int LastPositionSeconds,
    bool IsCompleted,
    bool JustCompleted,
    int CourseProgressPercent);

public record MyCourseDto(
    Guid CourseId,
    string CourseTitle,
    Guid OrganizationId,
    string OrganizationSlug,
    string OrganizationName,
    int ProgressPercent,
    Guid? LastLessonId,
    DateTime? LastAccessedAtUtc,
    DateTime? ExpiresAtUtc,
    bool IsExpired);

public record EnrollmentDto(
    Guid UserId,
    string UserName,
    string FullName,
    string Source,
    DateTime StartsAtUtc,
    DateTime? ExpiresAtUtc,
    int ProgressPercent);

public record EnrollMemberResultDto(string UserName, string Status, string? Error);

/// <summary>Student-side learning (player, progress, enrollment) inside the current organization.</summary>
public interface ILearningService
{
    Task<Result<LearnerCourseDto>> GetCourseAsync(Guid courseId, CancellationToken cancellationToken);
    Task<Result<LessonContentDto>> GetLessonAsync(Guid lessonId, CancellationToken cancellationToken);
    Task<Result<ProgressResultDto>> RecordProgressAsync(Guid lessonId, int? positionSeconds, int? watchedPercent, bool markCompleted, CancellationToken cancellationToken);

    Task<Result<MyCourseDto>> EnrollSelfAsync(Guid courseId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<EnrollMemberResultDto>>> EnrollMembersAsync(Guid courseId, IReadOnlyList<string> userNames, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<EnrollmentDto>>> ListEnrollmentsAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>All courses of the signed-in user across organizations (platform-level, no tenant).</summary>
    Task<IReadOnlyList<MyCourseDto>> GetMyCoursesAsync(CancellationToken cancellationToken);
}
