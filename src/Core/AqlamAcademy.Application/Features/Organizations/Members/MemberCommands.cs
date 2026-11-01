using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Organizations;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Organizations.Members;

/// <summary>Parses role names sent by clients. Owner can never be assigned this way.</summary>
public static class AssignableRoles
{
    public static readonly OrganizationRole[] All =
    [
        OrganizationRole.Admin, OrganizationRole.AcademicCoordinator, OrganizationRole.Teacher,
        OrganizationRole.Assistant, OrganizationRole.Finance, OrganizationRole.Student, OrganizationRole.Parent
    ];

    public static bool IsAssignable(string name) =>
        Enum.TryParse<OrganizationRole>(name, ignoreCase: true, out var role) && All.Contains(role);

    public static OrganizationRole Combine(IEnumerable<string> names) =>
        names.Aggregate(OrganizationRole.None, (acc, name) => acc | Enum.Parse<OrganizationRole>(name, ignoreCase: true));
}

public class RoleListValidator : AbstractValidator<IReadOnlyList<string>>
{
    public RoleListValidator()
    {
        RuleFor(roles => roles).NotEmpty().WithMessage("At least one role is required.");
        RuleForEach(roles => roles)
            .Must(AssignableRoles.IsAssignable)
            .WithMessage("'{PropertyValue}' is not an assignable role.");
    }
}

public record ListMembersQuery : IRequest<Result<IReadOnlyList<MemberDto>>>;

public class ListMembersQueryHandler(IOrganizationMembersService members, ICurrentOrganization organization)
    : IRequestHandler<ListMembersQuery, Result<IReadOnlyList<MemberDto>>>
{
    public async Task<Result<IReadOnlyList<MemberDto>>> Handle(ListMembersQuery request, CancellationToken cancellationToken) =>
        organization.OrganizationId is { } orgId
            ? Result<IReadOnlyList<MemberDto>>.Success(await members.ListAsync(orgId, cancellationToken))
            : Result<IReadOnlyList<MemberDto>>.NotFound("Organization not found.");
}

/// <summary>Adds an existing platform user (by username) to the organization with the given roles.</summary>
public record AddMemberCommand(string UserName, IReadOnlyList<string> Roles) : IRequest<Result<MemberDto>>;

public class AddMemberCommandValidator : AbstractValidator<AddMemberCommand>
{
    public AddMemberCommandValidator()
    {
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Roles).SetValidator(new RoleListValidator());
    }
}

public class AddMemberCommandHandler(IOrganizationMembersService members, ICurrentOrganization organization)
    : IRequestHandler<AddMemberCommand, Result<MemberDto>>
{
    public Task<Result<MemberDto>> Handle(AddMemberCommand request, CancellationToken cancellationToken) =>
        organization.OrganizationId is { } orgId
            ? members.AddExistingUserAsync(orgId, request.UserName.Trim(), AssignableRoles.Combine(request.Roles), cancellationToken)
            : Task.FromResult(Result<MemberDto>.NotFound("Organization not found."));
}

public record UpdateMemberRolesCommand(Guid UserId, IReadOnlyList<string> Roles) : IRequest<Result<MemberDto>>;

public class UpdateMemberRolesCommandValidator : AbstractValidator<UpdateMemberRolesCommand>
{
    public UpdateMemberRolesCommandValidator()
    {
        RuleFor(x => x.Roles).SetValidator(new RoleListValidator());
    }
}

public class UpdateMemberRolesCommandHandler(IOrganizationMembersService members, ICurrentOrganization organization)
    : IRequestHandler<UpdateMemberRolesCommand, Result<MemberDto>>
{
    public Task<Result<MemberDto>> Handle(UpdateMemberRolesCommand request, CancellationToken cancellationToken) =>
        organization.OrganizationId is { } orgId
            ? members.UpdateRolesAsync(orgId, request.UserId, AssignableRoles.Combine(request.Roles), cancellationToken)
            : Task.FromResult(Result<MemberDto>.NotFound("Organization not found."));
}

public record ApproveMemberCommand(Guid UserId) : IRequest<Result<MemberDto>>;

public class ApproveMemberCommandHandler(IOrganizationMembersService members, ICurrentOrganization organization)
    : IRequestHandler<ApproveMemberCommand, Result<MemberDto>>
{
    public Task<Result<MemberDto>> Handle(ApproveMemberCommand request, CancellationToken cancellationToken) =>
        organization.OrganizationId is { } orgId
            ? members.ApproveAsync(orgId, request.UserId, cancellationToken)
            : Task.FromResult(Result<MemberDto>.NotFound("Organization not found."));
}

public record RemoveMemberCommand(Guid UserId) : IRequest<Result<bool>>;

public class RemoveMemberCommandHandler(IOrganizationMembersService members, ICurrentOrganization organization)
    : IRequestHandler<RemoveMemberCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(RemoveMemberCommand request, CancellationToken cancellationToken) =>
        organization.OrganizationId is { } orgId
            ? members.RemoveAsync(orgId, request.UserId, cancellationToken)
            : Task.FromResult(Result<bool>.NotFound("Organization not found."));
}
