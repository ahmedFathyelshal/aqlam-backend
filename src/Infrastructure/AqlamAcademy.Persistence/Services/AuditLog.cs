using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class AuditLog(ApplicationDbContext context) : IAuditLog
{
    public async Task WriteAsync(string action, Guid? actorUserId, Guid? targetUserId, Guid? organizationId, string? details, CancellationToken cancellationToken)
    {
        context.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            OrganizationId = organizationId,
            Details = details is { Length: > 1000 } ? details[..1000] : details
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> ListAsync(string? action, string? user, int skip, int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 200);
        var query = context.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action))
        {
            var prefix = action.Trim();
            query = query.Where(a => a.Action.StartsWith(prefix));
        }
        if (!string.IsNullOrWhiteSpace(user))
        {
            var name = user.Trim().ToUpperInvariant();
            var ids = await context.Users.IgnoreQueryFilters().Where(u => u.NormalizedUserName == name).Select(u => u.Id).ToListAsync(cancellationToken);
            query = query.Where(a => (a.ActorUserId != null && ids.Contains(a.ActorUserId.Value)) || (a.TargetUserId != null && ids.Contains(a.TargetUserId.Value)));
        }

        var page = await query.OrderByDescending(a => a.AtUtc).Skip(Math.Max(skip, 0)).Take(take).ToListAsync(cancellationToken);

        var userIds = page.SelectMany(a => new[] { a.ActorUserId, a.TargetUserId }).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var orgIds = page.Where(a => a.OrganizationId != null).Select(a => a.OrganizationId!.Value).Distinct().ToList();
        var names = await context.Users.IgnoreQueryFilters().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName!, cancellationToken);
        var orgs = await context.Organizations.Where(o => orgIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Slug, cancellationToken);

        return page.Select(a => new AuditEntryDto(
            a.Id, a.AtUtc, a.Action,
            a.ActorUserId is { } actor ? names.GetValueOrDefault(actor) : null,
            a.TargetUserId is { } target ? names.GetValueOrDefault(target) : null,
            a.OrganizationId is { } org ? orgs.GetValueOrDefault(org) : null,
            a.Details)).ToList();
    }
}
