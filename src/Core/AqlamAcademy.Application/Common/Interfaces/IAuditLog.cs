namespace AqlamAcademy.Application.Common.Interfaces;

/// <summary>Writes the append-only audit trail. Details must never contain secrets.</summary>
public interface IAuditLog
{
    Task WriteAsync(string action, Guid? actorUserId, Guid? targetUserId, Guid? organizationId, string? details, CancellationToken cancellationToken);
}
