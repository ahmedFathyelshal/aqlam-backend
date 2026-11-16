namespace AqlamAcademy.Application.Common.Interfaces;

public record AuditEntryDto(Guid Id, DateTime AtUtc, string Action, string? Actor, string? Target, string? Organization, string? Details);

/// <summary>Writes and reads the append-only audit trail. Details must never contain secrets.</summary>
public interface IAuditLog
{
    Task WriteAsync(string action, Guid? actorUserId, Guid? targetUserId, Guid? organizationId, string? details, CancellationToken cancellationToken);

    /// <summary>Newest first. <paramref name="user"/> matches the actor's or target's username; <paramref name="action"/> is a prefix such as "password".</summary>
    Task<IReadOnlyList<AuditEntryDto>> ListAsync(string? action, string? user, int skip, int take, CancellationToken cancellationToken);
}
