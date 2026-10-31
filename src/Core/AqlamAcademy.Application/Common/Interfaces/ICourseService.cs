using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Application.Common.Interfaces;

public interface ICourseService
{
    Task<CourseDraftDto> CreateDraftAsync(Course course, CancellationToken cancellationToken);
    Task<CourseDraftDto?> GetDraftByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Course?> GetCourseByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateIdentityAsync(Guid id, string title, int? stageId, int? gradeId, int? subjectId, CancellationToken cancellationToken);
    Task UpdateCommercialsAsync(Guid id, PricingType? pricingType, decimal? price, bool codeActivationOnly, AccessExpiryPolicy? expiryPolicy, int? accessDurationDays, CancellationToken cancellationToken);
    Task UpdateMediaAsync(Guid id, string? subtitle, string? thumbnailUrl, string? promoVideoUrl, CancellationToken cancellationToken);
    Task UpdateCurriculumAsync(Guid id, string? syllabusRequirements, ICollection<CourseChapterDto> chapters, CancellationToken cancellationToken);
}
