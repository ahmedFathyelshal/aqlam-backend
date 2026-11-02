namespace AqlamAcademy.Domain.Communication;

public enum NotificationType
{
    Announcement = 1,
    Absence = 2,
    Late = 3
}

/// <summary>
/// An in-app message to one user. Lives in the platform store so a user (e.g. a parent with children in two
/// schools) has a single inbox. Rows are written once and only their read state changes.
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? OrganizationId { get; set; }

    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>Relative app link to open (e.g. /parent/{org}/children/{id}).</summary>
    public string? Link { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
}

/// <summary>Who an announcement goes to (combinable).</summary>
[Flags]
public enum AnnouncementAudience
{
    None = 0,
    Students = 1,
    Parents = 2,
    Staff = 4
}

public enum AnnouncementScope
{
    Organization = 1,
    Grade = 2,
    Class = 3
}

/// <summary>A message from the organization to a scope (whole org, a grade, or one class/group). Tenant store.</summary>
public class Announcement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public AnnouncementScope Scope { get; set; }
    public int? GradeId { get; set; }
    public Guid? ClassGroupId { get; set; }
    public AnnouncementAudience Audience { get; set; }
    public bool SendEmail { get; set; }

    public Guid AuthorUserId { get; set; }
    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;
    public int RecipientCount { get; set; }
}
