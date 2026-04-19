using Microsoft.AspNetCore.Identity;
using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Infrastructure.Persistence.Identity;

public class ApplicationUser : IdentityUser<Guid>, ISoftDelete
{
    public string FullName { get; set; } = string.Empty;
    
    // Audit properties
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    
    public DateTime? LastModifiedAtUtc { get; set; }
    public string? LastModifiedBy { get; set; }

    // Soft-delete properties
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}
