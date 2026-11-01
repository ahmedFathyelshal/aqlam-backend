using AqlamAcademy.Domain.Organizations;

namespace AqlamAcademy.Application.Common.Interfaces;

public record MembershipSummaryDto(
    Guid OrganizationId,
    string Slug,
    string Name,
    string Type,
    string CountryCode,
    IReadOnlyList<string> Roles);

public interface IMembershipService
{
    /// <summary>Roles of an active membership, or <see cref="OrganizationRole.None"/>. Cached briefly.</summary>
    Task<OrganizationRole> GetActiveRolesAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MembershipSummaryDto>> GetUserMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Drops the cached roles after a membership changes.</summary>
    void Invalidate(Guid userId, Guid organizationId);
}
