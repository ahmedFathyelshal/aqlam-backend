using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Learning;

public enum EnrollmentSource
{
    FreeEnrollment = 1,
    Manual = 2,          // added by the organization's staff
    ActivationCode = 3,
    Purchase = 4,
    ClassRoster = 5     // through a class/group the student belongs to
}

public enum EnrollmentStatus
{
    Active = 1,
    Revoked = 2
}

/// <summary>
/// A student's access to one course. Lives in the platform store (not the tenant store) because it is
/// needed across organizations ("My Courses"), by payments and by activation codes. Course title and
/// progress are snapshots kept up to date by the learning flow, so listing never queries tenant databases.
/// </summary>
public class Enrollment : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CourseId { get; set; }

    public string CourseTitle { get; set; } = string.Empty;

    public EnrollmentSource Source { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;

    public DateTime StartsAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Null = unlimited access.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public int ProgressPercent { get; set; }
    public Guid? LastLessonId { get; set; }
    public DateTime? LastAccessedAtUtc { get; set; }

    public bool IsUsable(DateTime now) =>
        Status == EnrollmentStatus.Active && (ExpiresAtUtc is null || ExpiresAtUtc > now);
}
