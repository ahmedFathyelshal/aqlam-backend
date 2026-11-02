using AqlamAcademy.Domain.Classes;
using System.Security.Cryptography;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Organizations.Members;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class OrganizationMembersService : IOrganizationMembersService
{
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I/L
    private const int CodeLength = 8;

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMembershipService _memberships;

    public OrganizationMembersService(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IMembershipService memberships)
    {
        _context = context;
        _userManager = userManager;
        _memberships = memberships;
    }

    public async Task<IReadOnlyList<MemberDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var rows = await (
                from m in _context.Memberships.AsNoTracking()
                join u in _context.Users.AsNoTracking() on m.UserId equals u.Id
                where m.OrganizationId == organizationId && m.Status != MembershipStatus.Left
                orderby m.JoinedAtUtc
                select new { Membership = m, u.UserName, u.FullName, u.Gender })
            .ToListAsync(cancellationToken);

        return rows.Select(r => ToDto(r.Membership, r.UserName!, r.FullName, r.Gender)).ToList();
    }

    public async Task<Result<MemberDto>> AddExistingUserAsync(Guid organizationId, string userName, OrganizationRole roles, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
            return Result<MemberDto>.NotFound($"User '{userName}' was not found.");

        var membership = await _context.Memberships
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == user.Id, cancellationToken);

        if (membership is { Status: not MembershipStatus.Left })
            return Result<MemberDto>.Conflict("This user is already a member of the organization.");

        if (membership is null)
        {
            membership = new Membership { OrganizationId = organizationId, UserId = user.Id, Source = MembershipSource.Invitation };
            _context.Memberships.Add(membership);
        }

        // Rejoining after leaving starts fresh with the new roles.
        membership.Roles = roles;
        membership.Status = MembershipStatus.Active;
        membership.JoinedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        _memberships.Invalidate(user.Id, organizationId);

        return Result<MemberDto>.Success(ToDto(membership, user.UserName!, user.FullName, user.Gender));
    }

    public async Task<Result<MemberDto>> UpdateRolesAsync(Guid organizationId, Guid userId, OrganizationRole roles, CancellationToken cancellationToken)
    {
        var (membership, user) = await FindCurrentMembershipAsync(organizationId, userId, cancellationToken);
        if (membership is null)
            return Result<MemberDto>.NotFound("Member not found.");

        // Ownership is never granted or removed through role edits.
        membership.Roles = roles | (membership.Roles & OrganizationRole.Owner);
        membership.LastModifiedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        _memberships.Invalidate(userId, organizationId);

        return Result<MemberDto>.Success(ToDto(membership, user!.UserName!, user.FullName, user.Gender));
    }

    public async Task<Result<MemberDto>> SetGenderAsync(Guid organizationId, Guid userId, Gender? gender, CancellationToken cancellationToken)
    {
        var (membership, user) = await FindCurrentMembershipAsync(organizationId, userId, cancellationToken);
        if (membership is null)
            return Result<MemberDto>.NotFound("Member not found.");

        user!.Gender = gender;
        await _userManager.UpdateAsync(user);

        return Result<MemberDto>.Success(ToDto(membership, user.UserName!, user.FullName, user.Gender));
    }

    public async Task<Result<MemberDto>> ApproveAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var (membership, user) = await FindCurrentMembershipAsync(organizationId, userId, cancellationToken);
        if (membership is null)
            return Result<MemberDto>.NotFound("Member not found.");
        if (membership.Status != MembershipStatus.Pending)
            return Result<MemberDto>.Conflict("This membership is not waiting for approval.");

        membership.Status = MembershipStatus.Active;
        await _context.SaveChangesAsync(cancellationToken);
        _memberships.Invalidate(userId, organizationId);

        return Result<MemberDto>.Success(ToDto(membership, user!.UserName!, user.FullName, user.Gender));
    }

    public async Task<Result<bool>> RemoveAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var (membership, _) = await FindCurrentMembershipAsync(organizationId, userId, cancellationToken);
        if (membership is null)
            return Result<bool>.NotFound("Member not found.");
        if (membership.Roles.HasFlag(OrganizationRole.Owner))
            return Result<bool>.Conflict("The owner cannot be removed from the organization.");

        // Kept as "Left" (not deleted) so history such as attendance and grades stays attributable.
        membership.Status = MembershipStatus.Left;
        await _context.SaveChangesAsync(cancellationToken);
        _memberships.Invalidate(userId, organizationId);

        return Result<bool>.Success(true);
    }

    public async Task<Result<JoinCodeDto>> CreateJoinCodeAsync(Guid organizationId, OrganizationRole role, bool requiresApproval, DateTime? expiresAtUtc, int? maxUses, CancellationToken cancellationToken)
    {
        var joinCode = new JoinCode
        {
            OrganizationId = organizationId,
            Role = role,
            RequiresApproval = requiresApproval,
            ExpiresAtUtc = expiresAtUtc,
            MaxUses = maxUses
        };

        // Collisions are astronomically unlikely (31^8 ≈ 8.5e11); the unique index has the final word.
        do joinCode.Code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
        while (await _context.JoinCodes.AnyAsync(c => c.Code == joinCode.Code, cancellationToken));

        _context.JoinCodes.Add(joinCode);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<JoinCodeDto>.Success(ToDto(joinCode));
    }

    public async Task<IReadOnlyList<JoinCodeDto>> ListJoinCodesAsync(Guid organizationId, CancellationToken cancellationToken) =>
        (await _context.JoinCodes.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken))
        .Select(ToDto)
        .ToList();

    public async Task<Result<bool>> DeactivateJoinCodeAsync(Guid organizationId, Guid joinCodeId, CancellationToken cancellationToken)
    {
        var joinCode = await _context.JoinCodes.FirstOrDefaultAsync(c => c.Id == joinCodeId && c.OrganizationId == organizationId, cancellationToken);
        if (joinCode is null)
            return Result<bool>.NotFound("Join code not found.");

        joinCode.IsActive = false;
        await _context.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<JoinResultDto>> JoinWithCodeAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().Replace("-", string.Empty).ToUpperInvariant();
        var now = DateTime.UtcNow;

        var joinCode = await _context.JoinCodes
            .Include(c => c.Organization)
            .FirstOrDefaultAsync(c => c.Code == normalized, cancellationToken);

        // One message for every invalid case, so codes cannot be probed.
        if (joinCode is null || !joinCode.IsActive
            || joinCode.Organization.Status != OrganizationStatus.Active
            || (joinCode.ExpiresAtUtc.HasValue && joinCode.ExpiresAtUtc.Value <= now))
            return Result<JoinResultDto>.NotFound("This code is not valid.");

        var membership = await _context.Memberships
            .FirstOrDefaultAsync(m => m.OrganizationId == joinCode.OrganizationId && m.UserId == userId, cancellationToken);

        if (membership is { Status: not MembershipStatus.Left } && membership.Roles.HasFlag(joinCode.Role))
            return Result<JoinResultDto>.Conflict("You are already a member of this organization.");

        // Consume one use atomically so concurrent joins cannot exceed MaxUses.
        var consumed = await _context.JoinCodes
            .Where(c => c.Id == joinCode.Id && (c.MaxUses == null || c.UsesCount < c.MaxUses))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsesCount, c => c.UsesCount + 1), cancellationToken);
        if (consumed == 0)
            return Result<JoinResultDto>.NotFound("This code is not valid.");

        var status = joinCode.RequiresApproval ? MembershipStatus.Pending : MembershipStatus.Active;

        if (membership is null)
        {
            membership = new Membership
            {
                OrganizationId = joinCode.OrganizationId,
                UserId = userId,
                Source = MembershipSource.JoinCode,
                Roles = joinCode.Role,
                Status = status
            };
            _context.Memberships.Add(membership);
        }
        else if (membership.Status == MembershipStatus.Left)
        {
            membership.Roles = joinCode.Role;
            membership.Status = status;
            membership.Source = MembershipSource.JoinCode;
            membership.JoinedAtUtc = now;
        }
        else
        {
            // An existing member (e.g. a teacher) gains the extra role; their current status is kept.
            membership.Roles |= joinCode.Role;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _memberships.Invalidate(userId, joinCode.OrganizationId);

        var org = joinCode.Organization;
        return Result<JoinResultDto>.Success(new JoinResultDto(org.Id, org.Slug, org.Name, org.Type.ToString(), membership.Status.ToString()));
    }

    private async Task<(Membership? Membership, ApplicationUser? User)> FindCurrentMembershipAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var membership = await _context.Memberships.FirstOrDefaultAsync(
            m => m.OrganizationId == organizationId && m.UserId == userId && m.Status != MembershipStatus.Left,
            cancellationToken);
        if (membership is null)
            return (null, null);

        var user = await _userManager.FindByIdAsync(userId.ToString());
        return (membership, user);
    }

    private static MemberDto ToDto(Membership m, string userName, string fullName, Gender? gender) => new(
        m.UserId,
        userName,
        fullName,
        Enum.GetValues<OrganizationRole>().Where(r => r != OrganizationRole.None && m.Roles.HasFlag(r)).Select(r => r.ToString()).ToList(),
        m.Status.ToString(),
        m.Source.ToString(),
        m.JoinedAtUtc,
        gender?.ToString());

    private static JoinCodeDto ToDto(JoinCode c) => new(
        c.Id, c.Code, c.Role.ToString(), c.RequiresApproval, c.ExpiresAtUtc, c.MaxUses, c.UsesCount, c.IsActive);
}
