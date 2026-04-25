using System.ComponentModel.DataAnnotations;

namespace AqlamAcademy.Infrastructure.Persistence.Identity;

public class UserRefreshToken
{
    [Key]
    public Guid Id { get; set; }
    
    public Guid UserId { get; set; }
    
    [Required]
    [MaxLength(256)]
    public string TokenHash { get; set; } = string.Empty;
    
    public TokenStatus Status { get; set; }
    
    public DateTime ExpiresAt { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public Guid? ReplacedByTokenId { get; set; }
    
    [MaxLength(250)]
    public string? ReasonRevoked { get; set; }

    // Device Audit Data
    [MaxLength(50)]
    public string? CreatedByIp { get; set; }
    
    [MaxLength(500)]
    public string? UserAgent { get; set; }
    
    [MaxLength(100)]
    public string? DeviceId { get; set; }

    public virtual ApplicationUser User { get; set; } = null!;
}
