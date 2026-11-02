using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Organizations;
using MediatR;

namespace AqlamAcademy.Application.Features.Platform.Organizations;

public record GetOrganizationsQuery(OrganizationType? Type = null) : IRequest<Result<IReadOnlyList<OrganizationDto>>>;

public class GetOrganizationsQueryHandler : IRequestHandler<GetOrganizationsQuery, Result<IReadOnlyList<OrganizationDto>>>
{
    private readonly IOrganizationAdminService _organizations;

    public GetOrganizationsQueryHandler(IOrganizationAdminService organizations)
    {
        _organizations = organizations;
    }

    public async Task<Result<IReadOnlyList<OrganizationDto>>> Handle(GetOrganizationsQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<OrganizationDto>>.Success(await _organizations.ListAsync(request.Type, cancellationToken));
}
