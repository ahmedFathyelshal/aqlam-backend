using AqlamAcademy.Domain.Organizations;
using Finbuckle.MultiTenant.Abstractions;

namespace AqlamAcademy.Infrastructure.Persistence.MultiTenancy;

/// <summary>
/// Resolved tenant for the current request. Id = Organization.Id, Identifier = Organization.Slug.
/// </summary>
public record AqlamTenantInfo : ITenantInfo
{
    public required string Id { get; init; }
    public required string Identifier { get; init; }
    public string? Name { get; init; }

    public OrganizationType Type { get; init; }
    public OrganizationStatus Status { get; init; }
    public IsolationTier IsolationTier { get; init; }

    /// <summary>Null for the shared tier: the shared connection string is used.</summary>
    public string? ConnectionString { get; init; }

    public Guid OrganizationId => Guid.Parse(Id);
}
