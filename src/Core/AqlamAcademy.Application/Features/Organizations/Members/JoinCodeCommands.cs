using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Organizations;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Organizations.Members;

public record CreateJoinCodeCommand(
    string Role = nameof(OrganizationRole.Student),
    bool RequiresApproval = false,
    DateTime? ExpiresAtUtc = null,
    int? MaxUses = null) : IRequest<Result<JoinCodeDto>>;

public class CreateJoinCodeCommandValidator : AbstractValidator<CreateJoinCodeCommand>
{
    public CreateJoinCodeCommandValidator()
    {
        // Join codes are for students and parents; staff are added explicitly by an admin.
        RuleFor(x => x.Role)
            .Must(role => Enum.TryParse<OrganizationRole>(role, true, out var parsed)
                          && parsed is OrganizationRole.Student or OrganizationRole.Parent)
            .WithMessage("Join codes can grant the Student or Parent role only.");
        RuleFor(x => x.MaxUses).GreaterThan(0).When(x => x.MaxUses.HasValue);
        RuleFor(x => x.ExpiresAtUtc).GreaterThan(_ => DateTime.UtcNow).When(x => x.ExpiresAtUtc.HasValue)
            .WithMessage("Expiry must be in the future.");
    }
}

public class CreateJoinCodeCommandHandler(IOrganizationMembersService members, ICurrentOrganization organization)
    : IRequestHandler<CreateJoinCodeCommand, Result<JoinCodeDto>>
{
    public Task<Result<JoinCodeDto>> Handle(CreateJoinCodeCommand request, CancellationToken cancellationToken) =>
        organization.OrganizationId is { } orgId
            ? members.CreateJoinCodeAsync(orgId, Enum.Parse<OrganizationRole>(request.Role, true), request.RequiresApproval,
                request.ExpiresAtUtc, request.MaxUses, cancellationToken)
            : Task.FromResult(Result<JoinCodeDto>.NotFound("Organization not found."));
}

public record ListJoinCodesQuery : IRequest<Result<IReadOnlyList<JoinCodeDto>>>;

public class ListJoinCodesQueryHandler(IOrganizationMembersService members, ICurrentOrganization organization)
    : IRequestHandler<ListJoinCodesQuery, Result<IReadOnlyList<JoinCodeDto>>>
{
    public async Task<Result<IReadOnlyList<JoinCodeDto>>> Handle(ListJoinCodesQuery request, CancellationToken cancellationToken) =>
        organization.OrganizationId is { } orgId
            ? Result<IReadOnlyList<JoinCodeDto>>.Success(await members.ListJoinCodesAsync(orgId, cancellationToken))
            : Result<IReadOnlyList<JoinCodeDto>>.NotFound("Organization not found.");
}

public record DeactivateJoinCodeCommand(Guid JoinCodeId) : IRequest<Result<bool>>;

public class DeactivateJoinCodeCommandHandler(IOrganizationMembersService members, ICurrentOrganization organization)
    : IRequestHandler<DeactivateJoinCodeCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(DeactivateJoinCodeCommand request, CancellationToken cancellationToken) =>
        organization.OrganizationId is { } orgId
            ? members.DeactivateJoinCodeAsync(orgId, request.JoinCodeId, cancellationToken)
            : Task.FromResult(Result<bool>.NotFound("Organization not found."));
}

/// <summary>The signed-in user joins an organization with a code (no organization context needed).</summary>
public record JoinOrganizationCommand(string Code) : IRequest<Result<JoinResultDto>>;

public class JoinOrganizationCommandValidator : AbstractValidator<JoinOrganizationCommand>
{
    public JoinOrganizationCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(16);
    }
}

public class JoinOrganizationCommandHandler(IOrganizationMembersService members, ICurrentUserService currentUser)
    : IRequestHandler<JoinOrganizationCommand, Result<JoinResultDto>>
{
    public Task<Result<JoinResultDto>> Handle(JoinOrganizationCommand request, CancellationToken cancellationToken) =>
        members.JoinWithCodeAsync(currentUser.UserId, request.Code, cancellationToken);
}
