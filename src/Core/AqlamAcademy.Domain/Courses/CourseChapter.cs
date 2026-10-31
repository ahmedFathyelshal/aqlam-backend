using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Courses;

public class CourseChapter : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    
    // Foreign Key
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;

    // Navigations
    public ICollection<CourseLesson> Lessons { get; set; } = new List<CourseLesson>();
}
