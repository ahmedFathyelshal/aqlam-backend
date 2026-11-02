using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.Commands;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Application.Common.Interfaces;

/// <summary>Course authoring inside the current organization (all queries are tenant-filtered).</summary>
public interface ICourseService
{
    Task<CourseDraftDto> CreateDraftAsync(Course course, CancellationToken cancellationToken);
    Task<CourseDraftDto?> GetDraftByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Course?> GetCourseByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CourseSummaryDto>> ListAsync(CancellationToken cancellationToken);

    Task UpdateIdentityAsync(Guid id, string title, int? curriculumId, int? stageId, int? gradeId, int? subjectId, CancellationToken cancellationToken);
    Task UpdateCommercialsAsync(Guid id, PricingType? pricingType, decimal? price, bool codeActivationOnly, AccessExpiryPolicy? expiryPolicy, int? accessDurationDays, CancellationToken cancellationToken);
    Task UpdateMediaAsync(Guid id, string? subtitle, string? thumbnailUrl, string? promoVideoUrl, CancellationToken cancellationToken);

    /// <summary>Non-destructive curriculum save (see <see cref="SyncCurriculumCommand"/>).</summary>
    Task<Result<CourseDraftDto>> SyncCurriculumAsync(SyncCurriculumCommand command, CancellationToken cancellationToken);

    /// <summary>Publish checklist; null when the course does not exist in this organization.</summary>
    Task<IReadOnlyList<string>?> GetPublishIssuesAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<CourseDraftDto>> ChangeStatusAsync(Guid id, CourseStatus status, CancellationToken cancellationToken);
    Task<Result<CourseDraftDto>> SetVisibilityAsync(Guid id, CourseVisibility visibility, CancellationToken cancellationToken);
}
