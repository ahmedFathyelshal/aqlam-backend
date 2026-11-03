using AqlamAcademy.Domain.Common;
using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Domain.Courses;

/// <summary>
/// Tenant-owned: every course belongs to one organization (TenantId is a shadow property managed by the
/// multi-tenant DbContext). Lookups (stage/grade/subject) live in the platform store, so they are plain ids here.
/// </summary>
public class Course : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? PromoVideoUrl { get; set; }

    public PricingType? PricingType { get; set; }
    public decimal? Price { get; set; }
    public bool CodeActivationOnly { get; set; }

    public AccessExpiryPolicy? AccessExpiryPolicy { get; set; }
    public int? AccessDurationDays { get; set; }

    public string? SyllabusRequirements { get; set; }

    public Enums.CourseStatus Status { get; set; } = Enums.CourseStatus.Draft;
    public CourseVisibility Visibility { get; set; } = CourseVisibility.Private;

    /// <summary>
    /// Incremented on every curriculum save; clients send the version they loaded so concurrent edits are
    /// detected (409) instead of one editor silently overwriting another. Also an EF concurrency token.
    /// </summary>
    public int CurriculumVersion { get; set; }

    /// <summary>The staff member who created the course (ownership is the organization's).</summary>
    public Guid CreatedByUserId { get; set; }

    // Platform lookup ids (no cross-store foreign keys)
    public int? CurriculumId { get; set; }
    public int? StageId { get; set; }
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }
    /// <summary>A school's own subject that is not in the lookup list.</summary>
    public string? CustomSubjectName { get; set; }

    // Child entities
    public ICollection<CourseChapter> Chapters { get; set; } = new List<CourseChapter>();
}
