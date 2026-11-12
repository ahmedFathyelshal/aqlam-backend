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

    // What counts as a concern or an achievement on parent and student pages
    public int FollowUpAbsentDays { get; set; } = 3;
    public int FollowUpLateDays { get; set; } = 5;
    public int GoodGradePercent { get; set; } = 75;
    public int LowGradePercent { get; set; } = 60;
    public int AchievementPercent { get; set; } = 85;

    // When attendance that has not been taken counts as late on the owner's overview (organization time)
    /// <summary>A school's daily attendance is late after this time.</summary>
    public TimeOnly AttendanceDueBy { get; set; } = new(10, 0);
    /// <summary>A center group's attendance is late this many minutes after the meeting starts.</summary>
    public int GroupAttendanceGraceMinutes { get; set; } = 30;

    /// <summary>
    /// Centers only: teachers may create their own courses. Schools never allow it (subjects and their courses
    /// come from the school's classes); a personal academy always does.
    /// </summary>
    public bool TeachersCreateCourses { get; set; }

    // Policies
    public PublicPublishingPolicy PublicPublishing { get; set; } = PublicPublishingPolicy.Allowed;

    /// <summary>Commission agreed with this organization; null uses the platform rate.</summary>
    public decimal? CommissionPercent { get; set; }

    /// <summary>Saudi users read Hijri and Gregorian dates side by side; elsewhere Gregorian.</summary>
    public static CalendarDisplay DefaultCalendarFor(string countryCode) =>
        countryCode == "SA" ? CalendarDisplay.Both : CalendarDisplay.Gregorian;

    public ICollection<OrganizationDomain> Domains { get; set; } = new List<OrganizationDomain>();
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}
