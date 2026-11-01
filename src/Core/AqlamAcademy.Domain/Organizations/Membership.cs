using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Organizations;

/// <summary>A user's roles inside one organization. Users are global; access is per membership.</summary>
public class Membership : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public OrganizationRole Roles { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public MembershipSource Source { get; set; }
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}
