using AqlamAcademy.Domain.Organizations;

namespace AqlamAcademy.Application.Common.Interfaces;

/// <summary>Reads organization-level policies (platform store) for decisions made in tenant features.</summary>
public interface IOrganizationPolicyReader
{
    Task<PublicPublishingPolicy?> GetPublicPublishingAsync(Guid organizationId, CancellationToken cancellationToken);
}
