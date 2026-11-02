using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Organizations;

/// <summary>A host name that resolves to an organization (subdomain or verified custom domain).</summary>
public class OrganizationDomain : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>Lower-case host, e.g. "nile.aqlam.app" or "learn.nile.edu.sa".</summary>
    public string Host { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}
