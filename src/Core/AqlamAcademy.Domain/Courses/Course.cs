using AqlamAcademy.Domain.Common;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Lookups;

namespace AqlamAcademy.Domain.Courses;

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
    
    public Guid TeacherId { get; set; }
    
    // Foreign Keys to Lookups
    public int? StageId { get; set; }
    public EducationStage? Stage { get; set; }
    
    public int? GradeId { get; set; }
    public EducationGrade? Grade { get; set; }
    
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    // Child entities
    public ICollection<CourseChapter> Chapters { get; set; } = new List<CourseChapter>();
}
