using AqlamAcademy.Application.Common.Models;

namespace AqlamAcademy.Application.Common.Interfaces;

public record PlatformUserDto(Guid Id, string UserName, string FullName, string? Email, string? Phone, bool Locked, bool MustChangePassword, DateTime CreatedAtUtc, IReadOnlyList<string> Organizations);

/// <summary>Support tools for platform admins. Every change is audited.</summary>
public interface IPlatformAdminService
{
    Task<IReadOnlyList<PlatformUserDto>> SearchUsersAsync(string? query, int take, CancellationToken cancellationToken);
    Task<Result> SetUserLockedAsync(Guid actorUserId, Guid userId, bool locked, CancellationToken cancellationToken);
    /// <summary>Suspended organizations vanish from sign-in contexts and the marketplace; their data is kept.</summary>
    Task<Result> SetOrganizationSuspendedAsync(Guid actorUserId, string slug, bool suspended, CancellationToken cancellationToken);
}
