using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class PlatformAdminService(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    IAuditLog audit,
    MarketplaceIndex marketplace) : IPlatformAdminService
{
    public async Task<IReadOnlyList<PlatformUserDto>> SearchUsersAsync(string? query, int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 100);
        var q = query?.Trim();
        var users = context.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            var upper = q.ToUpperInvariant();
            users = users.Where(u => u.NormalizedUserName!.Contains(upper) || (u.NormalizedEmail != null && u.NormalizedEmail.Contains(upper))
                                     || u.FullName.Contains(q) || (u.PhoneNumber != null && u.PhoneNumber.Contains(q)));
        }

        var page = await users.OrderBy(u => u.UserName).Take(take).ToListAsync(cancellationToken);
        var ids = page.Select(u => u.Id).ToList();
        var memberships = await (from m in context.Memberships.AsNoTracking()
                                 join o in context.Organizations.AsNoTracking() on m.OrganizationId equals o.Id
                                 where ids.Contains(m.UserId) && m.Status != MembershipStatus.Left
                                 select new { m.UserId, o.Slug }).ToListAsync(cancellationToken);

        return page.Select(u => new PlatformUserDto(
            u.Id, u.UserName!, u.FullName, string.IsNullOrWhiteSpace(u.Email) ? null : u.Email, u.PhoneNumber,
            u.LockoutEnd is { } end && end > DateTimeOffset.UtcNow, u.MustChangePassword, u.CreatedAtUtc,
            memberships.Where(m => m.UserId == u.Id).Select(m => m.Slug).Distinct().ToList())).ToList();
    }

    public async Task<Result> SetUserLockedAsync(Guid actorUserId, Guid userId, bool locked, CancellationToken cancellationToken)
    {
        if (actorUserId == userId)
            return Result.Failure(["You cannot lock your own account."]);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result.Failure(["User not found."]);

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null);
        if (locked)
        {
            var tokens = await context.UserRefreshTokens.Where(t => t.UserId == userId && t.Status == TokenStatus.Active).ToListAsync(cancellationToken);
            foreach (var t in tokens) { t.Status = TokenStatus.Revoked; t.ReasonRevoked = "Account locked by platform support."; }
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        await audit.WriteAsync(locked ? "user.locked" : "user.unlocked", actorUserId, userId, null, $"Account {user.UserName} {(locked ? "locked" : "unlocked")}.", cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetOrganizationSuspendedAsync(Guid actorUserId, string slug, bool suspended, CancellationToken cancellationToken)
    {
        var org = await context.Organizations.FirstOrDefaultAsync(o => o.Slug == slug, cancellationToken);
        if (org is null)
            return Result.Failure(["Organization not found."]);
        if (org.Status == OrganizationStatus.Archived)
            return Result.Failure(["An archived organization cannot be changed."]);

        org.Status = suspended ? OrganizationStatus.Suspended : OrganizationStatus.Active;
        await context.SaveChangesAsync(cancellationToken);
        await marketplace.RefreshOrganizationAsync(org.Id, cancellationToken);

        await audit.WriteAsync(suspended ? "organization.suspended" : "organization.reactivated", actorUserId, null, org.Id, $"Organization {slug} {(suspended ? "suspended" : "reactivated")}.", cancellationToken);
        return Result.Success();
    }
}
