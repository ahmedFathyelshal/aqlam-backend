using AqlamAcademy.Domain.Organizations;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.MultiTenancy;

/// <summary>
/// Opens one organization's tenant store outside a request for that organization: public marketplace pages,
/// payment notifications and start-up jobs have no {org} in their address. The caller disposes the context.
/// </summary>
public class TenantStores(ApplicationDbContext platform, ConnectionStringProtector protector, DatabaseSettings database)
{
    public async Task<TenantDbContext?> OpenAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var org = await platform.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => new { o.Id, o.Slug, o.Name, o.Type, o.Status, o.IsolationTier, o.DedicatedConnectionString })
            .FirstOrDefaultAsync(cancellationToken);
        if (org is null)
            return null;

        var dedicated = org.IsolationTier == IsolationTier.Dedicated && org.DedicatedConnectionString is not null
            ? protector.Unprotect(org.DedicatedConnectionString)
            : null;
        var accessor = new StaticMultiTenantContextAccessor<AqlamTenantInfo>(new AqlamTenantInfo
        {
            Id = org.Id.ToString(),
            Identifier = org.Slug,
            Name = org.Name,
            Type = org.Type,
            Status = org.Status,
            IsolationTier = org.IsolationTier,
            ConnectionString = dedicated
        });
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(dedicated ?? database.ConnectionString, x => x
                .MigrationsAssembly(DependencyInjection.MigrationsAssembly)
                .MigrationsHistoryTable(TenantDbContext.MigrationsHistoryTable))
            .Options;
        return new TenantDbContext(accessor, options);
    }
}
