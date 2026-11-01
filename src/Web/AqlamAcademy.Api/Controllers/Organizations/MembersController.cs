using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Organizations.Members;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Organizations;

/// <summary>Members of the current organization (from the {org} route or the organization's host).</summary>
[ApiController]
[Route("api/orgs/{org}/members")]
[Route("api/members")]
public class MembersController : ControllerBase
{
    private readonly ISender _mediator;

    public MembersController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [HasPermission(Permissions.MembersView)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ListMembersQuery(), cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> Add([FromBody] AddMemberCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(command, cancellationToken));

    [HttpPut("{userId:guid}/roles")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> UpdateRoles(Guid userId, [FromBody] UpdateRolesRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new UpdateMemberRolesCommand(userId, request.Roles), cancellationToken));

    [HttpPost("{userId:guid}/approve")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> Approve(Guid userId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ApproveMemberCommand(userId), cancellationToken));

    [HttpDelete("{userId:guid}")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new RemoveMemberCommand(userId), cancellationToken));

    public record UpdateRolesRequest(IReadOnlyList<string> Roles);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
