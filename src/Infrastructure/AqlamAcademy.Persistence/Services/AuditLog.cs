using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Domain.Organizations;

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
}
