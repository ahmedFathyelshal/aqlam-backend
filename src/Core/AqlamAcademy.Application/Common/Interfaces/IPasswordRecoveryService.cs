using AqlamAcademy.Application.Common.Models;

namespace AqlamAcademy.Application.Common.Interfaces;

public record TemporaryPasswordDto(string UserName, string TemporaryPassword);

/// <summary>Ways to get back into an account: a school admin, a linked parent, or an emailed link.</summary>
public interface IPasswordRecoveryService
{
    /// <summary>An organization admin sets a temporary password for a member; the member must change it at next sign-in.</summary>
    Task<Result<TemporaryPasswordDto>> ResetMemberPasswordAsync(Guid organizationId, Guid actorUserId, Guid targetUserId, CancellationToken cancellationToken);

    /// <summary>A linked parent sets a temporary password for their child.</summary>
    Task<Result<TemporaryPasswordDto>> ResetChildPasswordAsync(Guid parentUserId, Guid childUserId, CancellationToken cancellationToken);

    /// <summary>Emails a reset link when the account has an email. Always succeeds so accounts cannot be probed.</summary>
    Task RequestEmailResetAsync(string identifier, CancellationToken cancellationToken);

    Task<Result> ConfirmEmailResetAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken);
}
