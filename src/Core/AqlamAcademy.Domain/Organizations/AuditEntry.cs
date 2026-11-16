namespace AqlamAcademy.Domain.Organizations;

/// <summary>
/// An append-only record of a sensitive action (password resets, role changes, suspensions, fee changes).
/// Never updated or deleted by the application; only platform admins read it.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Who did it; null for a self-service action by an anonymous visitor (e.g. an emailed reset link).</summary>
    public Guid? ActorUserId { get; set; }
    /// <summary>Short stable code such as "password.reset.admin".</summary>
    public string Action { get; set; } = string.Empty;
    public Guid? TargetUserId { get; set; }
    public Guid? OrganizationId { get; set; }
    /// <summary>Human-readable detail. Never contains passwords or tokens.</summary>
    public string? Details { get; set; }
}
