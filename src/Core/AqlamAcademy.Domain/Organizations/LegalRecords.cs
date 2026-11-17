namespace AqlamAcademy.Domain.Organizations;

/// <summary>The current version of each legal text. Changing a version asks everyone to accept again.</summary>
public static class LegalVersions
{
    public const string Terms = "2026-11-draft-1";
    public const string Privacy = "2026-11-draft-1";

    public const string TermsDocument = "terms";
    public const string PrivacyDocument = "privacy";

    public static string Current(string document) => document == TermsDocument ? Terms : Privacy;
}

/// <summary>One user's acceptance of one version of one legal text. Kept as evidence; never edited.</summary>
public class LegalAcceptance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Document { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Set when a parent accepted for a child (under 13, or when creating the child's account).</summary>
    public Guid? AcceptedByUserId { get; set; }
}

public enum DataRequestType
{
    Export = 1,
    Delete = 2
}

public enum DataRequestStatus
{
    Open = 1,
    Completed = 2,
    Rejected = 3
}

/// <summary>A user's request to receive or delete their data. Handled by a platform admin; recorded in the audit log.</summary>
public class DataRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DataRequestType Type { get; set; }
    public DataRequestStatus Status { get; set; } = DataRequestStatus.Open;
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? HandledAtUtc { get; set; }
    public Guid? HandledByUserId { get; set; }
}
