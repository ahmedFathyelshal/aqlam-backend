using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Application.Features.Teacher.Courses.DTOs;

public record CourseDraftDto
{
    public Guid? Id { get; init; }
    public string Status { get; init; } = "Draft";

    public string? Title { get; init; }
    public string? Slug { get; init; }
    public int? CurriculumId { get; init; }
    public int? StageId { get; init; }
    public int? GradeId { get; init; }
    public int? SubjectId { get; init; }

    public string? PricingType { get; init; }
    public decimal? Price { get; init; }
    public bool? CodeActivationOnly { get; init; }
    public string? AccessExpiryPolicy { get; init; }
    public int? AccessDurationDays { get; init; }

    public string? Subtitle { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? PromoVideoUrl { get; init; }

    public string? SyllabusRequirements { get; init; }
    public ICollection<CourseChapterDto> Chapters { get; init; } = new List<CourseChapterDto>();
}

public record CourseChapterDto
{
    public Guid? Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public int Order { get; init; }
    public ICollection<CourseLessonDto> Lessons { get; init; } = new List<CourseLessonDto>();
}

public record CourseLessonDto
{
    public Guid? Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public int Order { get; init; }
    public bool IsPublished { get; init; }
    public string Type { get; init; } = nameof(LessonType.Video);
    public int? DurationInMinutes { get; init; }
    public bool IsFreePreview { get; init; }
    public string? VideoUrl { get; init; }
    public string? Summary { get; init; }
    public string? Content { get; init; }
    public int UnlockDaysAfterEnrollment { get; init; }
    public bool RequirePreviousLessonCompletion { get; init; }
    public ICollection<LessonResourceDto> Resources { get; init; } = new List<LessonResourceDto>();
}

public record LessonResourceDto
{
    public Guid? Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string ResourceUrl { get; init; } = string.Empty;
    public string Type { get; init; } = nameof(ResourceType.File);
    public bool IsDownloadable { get; init; } = true;
}
