using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class MembershipService : IMembershipService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    public MembershipService(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<OrganizationRole> GetActiveRolesAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(CacheKey(userId, organizationId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            return await _context.Memberships.AsNoTracking()
                .Where(m => m.UserId == userId
                         && m.OrganizationId == organizationId
                         && m.Status == MembershipStatus.Active
                         && m.Organization.Status == OrganizationStatus.Active)
                .Select(m => m.Roles)
                .FirstOrDefaultAsync(cancellationToken);
        });
    }

    public void Invalidate(Guid userId, Guid organizationId) => _cache.Remove(CacheKey(userId, organizationId));

    private static string CacheKey(Guid userId, Guid organizationId) => $"membership:{userId}:{organizationId}";

    public async Task<IReadOnlyList<MembershipSummaryDto>> GetUserMembershipsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var memberships = await _context.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId
                     && m.Status == MembershipStatus.Active
                     && m.Organization.Status == OrganizationStatus.Active)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => new { m.OrganizationId, m.Organization.Slug, m.Organization.Name, m.Organization.Type, m.Organization.CountryCode, m.Roles })
            .ToListAsync(cancellationToken);

        return memberships
            .Select(m => new MembershipSummaryDto(
                m.OrganizationId,
                m.Slug,
                m.Name,
                m.Type.ToString(),
                m.CountryCode,
                Enum.GetValues<OrganizationRole>()
                    .Where(r => r != OrganizationRole.None && m.Roles.HasFlag(r))
                    .Select(r => r.ToString())
                    .ToList()))
            .ToList();
    }
}
