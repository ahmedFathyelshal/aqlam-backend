using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Platform.Organizations;
using AqlamAcademy.Domain.Organizations;

namespace AqlamAcademy.Application.Common.Interfaces;

/// <summary>Platform-administrator operations on organizations (tenants).</summary>
public interface IOrganizationAdminService
{
    Task<Result<OrganizationDto>> CreateAsync(
        string name,
        string slug,
        OrganizationType type,
        string countryCode,
        string ownerUserName,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationDto>> ListAsync(OrganizationType? type, CancellationToken cancellationToken);
}
