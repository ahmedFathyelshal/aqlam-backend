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
    public CalendarDisplay Calendar { get; set; } = CalendarDisplay.Both;
    /// <summary>First day of the school week (Sunday in Saudi Arabia and most Egyptian schools).</summary>
    public DayOfWeek WeekStartDay { get; set; } = DayOfWeek.Sunday;
    public Academics.GradingScale GradingScale { get; set; } = Academics.GradingScale.Descriptive;

    // Branding
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }

    // Contact details shown to the organization's parents
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    // Policies
    public PublicPublishingPolicy PublicPublishing { get; set; } = PublicPublishingPolicy.Allowed;

    /// <summary>Saudi users read Hijri and Gregorian dates side by side; elsewhere Gregorian.</summary>
    public static CalendarDisplay DefaultCalendarFor(string countryCode) =>
        countryCode == "SA" ? CalendarDisplay.Both : CalendarDisplay.Gregorian;

    public ICollection<OrganizationDomain> Domains { get; set; } = new List<OrganizationDomain>();
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}
