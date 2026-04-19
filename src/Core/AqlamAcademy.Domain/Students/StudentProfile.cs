using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Students;

public class StudentProfile : BaseEntity
{
    public Guid UserId { get; set; }
    
    public string? Grade { get; set; }
    public string? SchoolName { get; set; }
}
