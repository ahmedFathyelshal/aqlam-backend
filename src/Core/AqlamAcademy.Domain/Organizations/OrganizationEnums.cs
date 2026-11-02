namespace AqlamAcademy.Domain.Organizations;

public enum OrganizationType
{
    Personal = 1, // an independent teacher's own academy (created automatically)
    Center = 2,
    School = 3
}

public enum OrganizationStatus
{
    Active = 1,
    Suspended = 2,
    Archived = 3
}

/// <summary>How dates are shown to the organization's users.</summary>
public enum CalendarDisplay
{
    Gregorian = 1,
    Hijri = 2,
    Both = 3
}

/// <summary>Where the organization's tenant data is stored.</summary>
public enum IsolationTier
{
    Shared = 1,
    Dedicated = 2
}

/// <summary>Who may publish courses to the public marketplace.</summary>
public enum PublicPublishingPolicy
{
    Disabled = 1,
    RequiresAdminApproval = 2,
    Allowed = 3
}

[Flags]
public enum OrganizationRole
{
    None = 0,
    Owner = 1 << 0,
    Admin = 1 << 1,
    AcademicCoordinator = 1 << 2,
    Teacher = 1 << 3,
    Assistant = 1 << 4,
    Student = 1 << 5,
    Parent = 1 << 6,
    Finance = 1 << 7
}

public enum MembershipStatus
{
    Pending = 1,
    Active = 2,
    Suspended = 3,
    Left = 4
}

/// <summary>How the member joined; used for reporting (e.g. online vs center students).</summary>
public enum MembershipSource
{
    Owner = 1,
    Invitation = 2,
    PublicEnrollment = 3,
    ActivationCode = 4,
    JoinCode = 5,
    BulkImport = 6,
    Sso = 7,
    ParentLink = 8      // a parent linked to a student of the organization
}
