using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Staff;

public class TeacherProfile : BaseEntity
{
    public Guid UserId { get; set; }
    
    public string? Bio { get; set; }
    public string? Specialization { get; set; }
    
    // Many-to-Many relationship with Assistants
    public ICollection<TeacherAssistant> Assistants { get; set; } = new List<TeacherAssistant>();
}
