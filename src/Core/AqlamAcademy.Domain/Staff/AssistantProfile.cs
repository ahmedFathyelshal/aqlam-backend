using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Staff;

public class AssistantProfile : BaseEntity
{
    public Guid UserId { get; set; }
    
    // Many-to-Many relationship with Teachers
    public ICollection<TeacherAssistant> Teachers { get; set; } = new List<TeacherAssistant>();
}
