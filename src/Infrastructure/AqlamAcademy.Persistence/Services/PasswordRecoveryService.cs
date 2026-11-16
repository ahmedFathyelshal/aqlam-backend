using System.Security.Cryptography;
using System.Text;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using AqlamAcademy.Infrastructure.Persistence.Messaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class PasswordRecoveryService(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    IAuditLog audit,
    EmailQueue emailQueue,
    IConfiguration configuration) : IPasswordRecoveryService
{
    private const string PasswordAlphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const string InvalidLink = "This link is not valid or has expired.";

    public async Task<Result<TemporaryPasswordDto>> ResetMemberPasswordAsync(Guid organizationId, Guid actorUserId, Guid targetUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == targetUserId)
            return Result<TemporaryPasswordDto>.Conflict("Use change password for your own account.");

        var target = await context.Memberships.AsNoTracking().FirstOrDefaultAsync(
            m => m.OrganizationId == organizationId && m.UserId == targetUserId && m.Status != MembershipStatus.Left, cancellationToken);
        if (target is null)
            return Result<TemporaryPasswordDto>.NotFound("Member not found.");

        var actor = await context.Memberships.AsNoTracking().FirstOrDefaultAsync(
            m => m.OrganizationId == organizationId && m.UserId == actorUserId && m.Status == MembershipStatus.Active, cancellationToken);
        var actorIsOwner = actor?.Roles.HasFlag(OrganizationRole.Owner) == true;

        // An owner can be reset only by platform support; an admin only by the owner.
        if (target.Roles.HasFlag(OrganizationRole.Owner))
            return Result<TemporaryPasswordDto>.Conflict("The owner's password can only be reset by platform support.");
        if (target.Roles.HasFlag(OrganizationRole.Admin) && !actorIsOwner)
            return Result<TemporaryPasswordDto>.Conflict("Only the owner can reset an admin's password.");

        var user = await userManager.FindByIdAsync(targetUserId.ToString());
        if (user is null || user.IsDeleted)
            return Result<TemporaryPasswordDto>.NotFound("Member not found.");

        var password = await SetTemporaryPasswordAsync(user, cancellationToken);
        if (password is null)
            return Result<TemporaryPasswordDto>.Failure(ResultErrorType.Validation, "Could not reset the password.");

        await audit.WriteAsync("password.reset.admin", actorUserId, targetUserId, organizationId, $"Temporary password set for {user.UserName}.", cancellationToken);
        return Result<TemporaryPasswordDto>.Success(new TemporaryPasswordDto(user.UserName!, password));
    }

    public async Task<Result<TemporaryPasswordDto>> ResetChildPasswordAsync(Guid parentUserId, Guid childUserId, CancellationToken cancellationToken)
    {
        var linked = await context.ParentLinks.AsNoTracking().AnyAsync(l => l.ParentUserId == parentUserId && l.ChildUserId == childUserId, cancellationToken);
        if (!linked)
            return Result<TemporaryPasswordDto>.NotFound("Child not found.");

        var user = await userManager.FindByIdAsync(childUserId.ToString());
        if (user is null || user.IsDeleted)
            return Result<TemporaryPasswordDto>.NotFound("Child not found.");

        var password = await SetTemporaryPasswordAsync(user, cancellationToken);
        if (password is null)
            return Result<TemporaryPasswordDto>.Failure(ResultErrorType.Validation, "Could not reset the password.");

        await audit.WriteAsync("password.reset.parent", parentUserId, childUserId, null, $"Temporary password set for {user.UserName}.", cancellationToken);
        return Result<TemporaryPasswordDto>.Success(new TemporaryPasswordDto(user.UserName!, password));
    }

    public async Task RequestEmailResetAsync(string identifier, CancellationToken cancellationToken)
    {
        identifier = identifier.Trim();
        if (identifier.Length == 0)
            return;

        var user = await userManager.FindByNameAsync(identifier) ?? await userManager.FindByEmailAsync(identifier);
        if (user is null || user.IsDeleted || string.IsNullOrWhiteSpace(user.Email))
            return;

        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await userManager.GeneratePasswordResetTokenAsync(user)));
        var baseUrl = (configuration["App:PublicBaseUrl"] ?? configuration["Cors:AllowedOrigins:0"] ?? "http://localhost:4200").TrimEnd('/');
        var link = $"{baseUrl}/auth/reset-password?user={user.Id}&token={token}";

        emailQueue.Enqueue(new EmailMessage(
            user.Email,
            "إعادة تعيين كلمة المرور — أقلام / Reset your password — Aqlam",
            $"لإعادة تعيين كلمة المرور افتح الرابط التالي (صالح لمدة محدودة):\n{link}\n\nإذا لم تطلب ذلك فتجاهل هذه الرسالة.\n\n" +
            $"To reset your password open this link (valid for a limited time):\n{link}\n\nIf you did not ask for this, ignore this message."));

        await audit.WriteAsync("password.reset.email-requested", null, user.Id, null, "Reset link emailed.", cancellationToken);
    }

    public async Task<Result> ConfirmEmailResetAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDeleted)
            return Result.Failure([InvalidLink]);

        string raw;
        try { raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return Result.Failure([InvalidLink]); }

        var result = await userManager.ResetPasswordAsync(user, raw, newPassword);
        if (!result.Succeeded)
        {
            // Password-policy errors are useful to show; token errors are not.
            var policy = result.Errors.Where(e => e.Code.StartsWith("Password", StringComparison.Ordinal)).Select(e => e.Description).ToList();
            return Result.Failure(policy.Count > 0 ? policy : [InvalidLink]);
        }

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);
        await RevokeSessionsAsync(user.Id, cancellationToken);
        await audit.WriteAsync("password.reset.email-completed", null, user.Id, null, "Password changed through an emailed link.", cancellationToken);
        return Result.Success();
    }

    private async Task<string?> SetTemporaryPasswordAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var password = RandomNumberGenerator.GetString(PasswordAlphabet, 9) + RandomNumberGenerator.GetInt32(2, 10);
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
            return null;

        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
        await RevokeSessionsAsync(user.Id, cancellationToken);
        return password;
    }

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tokens = await context.UserRefreshTokens.Where(t => t.UserId == userId && t.Status == TokenStatus.Active).ToListAsync(cancellationToken);
        foreach (var t in tokens)
        {
            t.Status = TokenStatus.Revoked;
            t.ReasonRevoked = "Password was reset.";
        }
        if (tokens.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
    }
}
