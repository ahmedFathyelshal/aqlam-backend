using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class OrganizationPolicyReader(ApplicationDbContext context) : IOrganizationPolicyReader
{
    public async Task<PublicPublishingPolicy?> GetPublicPublishingAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await context.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => (PublicPublishingPolicy?)o.PublicPublishing)
            .FirstOrDefaultAsync(cancellationToken);
}
