using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Organizations;

/// <summary>
/// The tenant: an independent teacher's personal academy, a center or a school.
/// Lives in the platform store; all teaching data belongs to exactly one organization.
/// </summary>
public class Organization : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public OrganizationType Type { get; set; }
    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;

    // Isolation
    public IsolationTier IsolationTier { get; set; } = IsolationTier.Shared;
    /// <summary>Only for <see cref="IsolationTier.Dedicated"/>; must be stored encrypted (P2).</summary>
    public string? DedicatedConnectionString { get; set; }

    // Locale
    public string CountryCode { get; set; } = "SA";
    public string Currency { get; set; } = "SAR";
    public string TimeZone { get; set; } = "Asia/Riyadh";

    // Branding
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }

    // Policies
    public PublicPublishingPolicy PublicPublishing { get; set; } = PublicPublishingPolicy.Allowed;

    public ICollection<OrganizationDomain> Domains { get; set; } = new List<OrganizationDomain>();
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}
