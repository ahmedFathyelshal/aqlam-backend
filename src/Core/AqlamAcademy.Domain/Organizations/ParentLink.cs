namespace AqlamAcademy.Domain.Organizations;

/// <summary>
/// A verified parent ↔ child relationship (platform store: one family across organizations).
/// A child has at most <see cref="MaxParentsPerChild"/> parents.
/// </summary>
public class ParentLink
{
    public const int MaxParentsPerChild = 2;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParentUserId { get; set; }
    public Guid ChildUserId { get; set; }

    /// <summary>
    /// The organization that verified the link (by admin action or its invite code); null for a family link made
    /// outside any school (the child's own family code, or a parent who created the child's account).
    /// </summary>
    public Guid? OrganizationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A one-time code an organization gives a student's family (printed slip, message) so a parent can link
/// their own account to the child. The code itself is the verification until SMS OTP is available.
/// </summary>
public class ParentInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Null when the student made the code for their own parent (marketplace families).</summary>
    public Guid? OrganizationId { get; set; }
    public Guid ChildUserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public Guid? UsedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
