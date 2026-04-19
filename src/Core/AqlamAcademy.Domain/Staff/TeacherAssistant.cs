using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Staff;

public class TeacherAssistant : BaseEntity
{
    public Guid TeacherProfileId { get; set; }
    public TeacherProfile TeacherProfile { get; set; } = null!;

    public Guid AssistantProfileId { get; set; }
    public AssistantProfile AssistantProfile { get; set; } = null!;
    
    // Additional tracking requested
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}
