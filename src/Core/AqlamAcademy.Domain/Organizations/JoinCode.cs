using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Organizations;

/// <summary>
/// A short code (or QR) a center/school hands out so students can join the organization themselves.
/// </summary>
public class JoinCode : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>8 characters from an unambiguous alphabet (no 0/O, 1/I/L).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Role granted on joining (normally Student).</summary>
    public OrganizationRole Role { get; set; } = OrganizationRole.Student;

    /// <summary>When true, new members stay Pending until an admin approves them.</summary>
    public bool RequiresApproval { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }
    public int? MaxUses { get; set; }
    public int UsesCount { get; set; }
    public bool IsActive { get; set; } = true;
}
