using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;
using Finbuckle.MultiTenant.AspNetCore.Extensions;

namespace AqlamAcademy.Api.Infrastructure;

public class CurrentOrganization(IHttpContextAccessor httpContextAccessor) : ICurrentOrganization
{
    public Guid? OrganizationId => httpContextAccessor.HttpContext?.GetTenantInfo<AqlamTenantInfo>()?.OrganizationId;
}
