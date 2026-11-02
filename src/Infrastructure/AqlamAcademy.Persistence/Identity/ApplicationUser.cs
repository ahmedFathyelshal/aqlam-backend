using Microsoft.AspNetCore.Identity;
using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Infrastructure.Persistence.Identity;

public class ApplicationUser : IdentityUser<Guid>, ISoftDelete
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Full name, or the username when no name was given (self-registration does not ask for one). Not mapped.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? UserName ?? string.Empty : FullName;

    /// <summary>Needed for gender-separated classes (Saudi schools); optional elsewhere.</summary>
    public AqlamAcademy.Domain.Classes.Gender? Gender { get; set; }

    /// <summary>Set for accounts created by an administrator (e.g. bulk import) with a temporary password.</summary>
    public bool MustChangePassword { get; set; }
    
    // Audit properties
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    
    public DateTime? LastModifiedAtUtc { get; set; }
    public string? LastModifiedBy { get; set; }

    // Soft-delete properties
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    // Relationships
    public virtual ICollection<UserRefreshToken> RefreshTokens { get; set; } = new List<UserRefreshToken>();
}
