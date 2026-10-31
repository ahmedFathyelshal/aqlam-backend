using AqlamAcademy.Domain.Common;
using AqlamAcademy.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace AqlamAcademy.Domain.Courses;

public class CourseLesson : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public int Order { get; set; }

    public bool IsPublished { get; set; } = false;

    public LessonType Type { get; set; }

    public int? DurationInMinutes { get; set; }

    public bool IsFreePreview { get; set; } = false;

    public string? VideoUrl { get; set; }

    [MaxLength(500)]
    public string? Summary { get; set; }

    public string? Content { get; set; }

    public int UnlockDaysAfterEnrollment { get; set; } = 0;
    public bool RequirePreviousLessonCompletion { get; set; } = false;

    // Foreign Key mapping
    public Guid ChapterId { get; set; }
    public CourseChapter Chapter { get; set; } = null!;

    public virtual ICollection<LessonResource> Resources { get; set; } = new List<LessonResource>();
}
