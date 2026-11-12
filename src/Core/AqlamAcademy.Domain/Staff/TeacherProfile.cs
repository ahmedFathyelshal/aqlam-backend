using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Staff;

public class TeacherProfile : BaseEntity
{
    public Guid UserId { get; set; }

    public string? Bio { get; set; }
    public string? Specialization { get; set; }

    // Public profile of an independent teacher (marketplace)
    /// <summary>The name visitors see; usernames are never shown publicly.</summary>
    public string? PublicName { get; set; }
    /// <summary>One line under the name, e.g. "معلمة رياضيات للمرحلة الثانوية".</summary>
    public string? Headline { get; set; }
    public string? City { get; set; }
    /// <summary>"Show me in search". Needs a public name and the teacher's gender.</summary>
    public bool IsListed { get; set; }
    public DateTime? PhotoUpdatedAtUtc { get; set; }

    // Many-to-Many relationship with Assistants
    public ICollection<TeacherAssistant> Assistants { get; set; } = new List<TeacherAssistant>();
}

/// <summary>An independent teacher's uploaded photo; same rules and storage as organization logos.</summary>
public class TeacherPhoto
{
    public Guid UserId { get; set; }
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public static string PublicPath(string slug, DateTime updatedAtUtc) => $"/api/marketplace/teachers/{slug}/photo?v={updatedAtUtc.Ticks}";
}
