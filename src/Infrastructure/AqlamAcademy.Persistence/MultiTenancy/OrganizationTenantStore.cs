using AqlamAcademy.Domain.Organizations;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqlamAcademy.Infrastructure.Persistence.MultiTenancy;

/// <summary>
/// Read-only Finbuckle store over the Organizations table. An identifier containing a dot is treated as a
/// host name (subdomain or custom domain); otherwise it is an organization slug. Results are cached briefly
/// because resolution runs on every tenant-scoped request.
/// </summary>
public class OrganizationTenantStore : IMultiTenantStore<AqlamTenantInfo>
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    public OrganizationTenantStore(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public static string CacheKey(string identifier) => $"tenant:identifier:{identifier.ToLowerInvariant()}";

    public async Task<AqlamTenantInfo?> GetByIdentifierAsync(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var key = identifier.Trim().ToLowerInvariant();

        if (_cache.TryGetValue(CacheKey(key), out AqlamTenantInfo? cached))
            return cached;

        var query = _context.Organizations.AsNoTracking()
            .Where(o => o.Status == OrganizationStatus.Active);

        var organization = key.Contains('.')
            ? await query.FirstOrDefaultAsync(o => o.Domains.Any(d => d.Host == key))
            : await query.FirstOrDefaultAsync(o => o.Slug == key);

        // Only found tenants are cached: caching a miss would hide a newly created organization.
        if (organization is null)
            return null;

        var tenant = ToTenantInfo(organization);
        _cache.Set(CacheKey(key), tenant, CacheDuration);
        return tenant;
    }

    public async Task<AqlamTenantInfo?> GetAsync(string id)
    {
        if (!Guid.TryParse(id, out var organizationId))
            return null;

        var organization = await _context.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.Status == OrganizationStatus.Active);

        return organization is null ? null : ToTenantInfo(organization);
    }

    public async Task<IEnumerable<AqlamTenantInfo>> GetAllAsync() =>
        (await _context.Organizations.AsNoTracking().ToListAsync()).Select(ToTenantInfo);

    public async Task<IEnumerable<AqlamTenantInfo>> GetAllAsync(int take, int skip) =>
        (await _context.Organizations.AsNoTracking().OrderBy(o => o.CreatedAtUtc).Skip(skip).Take(take).ToListAsync())
            .Select(ToTenantInfo);

    // Organizations are created and changed through the application, never through the tenant store.
    public Task<bool> AddAsync(AqlamTenantInfo tenantInfo) => throw new NotSupportedException();
    public Task<bool> UpdateAsync(AqlamTenantInfo tenantInfo) => throw new NotSupportedException();
    public Task<bool> RemoveAsync(string identifier) => throw new NotSupportedException();

    private static AqlamTenantInfo ToTenantInfo(Organization organization) => new()
    {
        Id = organization.Id.ToString(),
        Identifier = organization.Slug,
        Name = organization.Name,
        Type = organization.Type,
        Status = organization.Status,
        IsolationTier = organization.IsolationTier,
        ConnectionString = organization.IsolationTier == IsolationTier.Dedicated ? organization.DedicatedConnectionString : null
    };
}
