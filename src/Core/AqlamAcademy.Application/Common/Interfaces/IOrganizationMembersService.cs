using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Organizations.Members;
using AqlamAcademy.Domain.Organizations;

namespace AqlamAcademy.Application.Common.Interfaces;

/// <summary>Membership management inside one organization, plus self-service joining by code.</summary>
public interface IOrganizationMembersService
{
    Task<IReadOnlyList<MemberDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<Result<MemberDto>> AddExistingUserAsync(Guid organizationId, string userName, OrganizationRole roles, CancellationToken cancellationToken);
    Task<Result<MemberDto>> UpdateRolesAsync(Guid organizationId, Guid userId, OrganizationRole roles, CancellationToken cancellationToken);
    Task<Result<MemberDto>> SetGenderAsync(Guid organizationId, Guid userId, AqlamAcademy.Domain.Classes.Gender? gender, CancellationToken cancellationToken);
    Task<Result<MemberDto>> ApproveAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    Task<Result<bool>> RemoveAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task<Result<JoinCodeDto>> CreateJoinCodeAsync(Guid organizationId, OrganizationRole role, bool requiresApproval, DateTime? expiresAtUtc, int? maxUses, CancellationToken cancellationToken);
    Task<IReadOnlyList<JoinCodeDto>> ListJoinCodesAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<Result<bool>> DeactivateJoinCodeAsync(Guid organizationId, Guid joinCodeId, CancellationToken cancellationToken);
    Task<Result<JoinResultDto>> JoinWithCodeAsync(Guid userId, string code, CancellationToken cancellationToken);
}
