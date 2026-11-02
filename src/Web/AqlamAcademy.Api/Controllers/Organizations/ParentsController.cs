using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Parents;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Organizations;

/// <summary>Parents of a student, managed by the organization's staff.</summary>
[ApiController]
[Route("api/orgs/{org}/members/{studentId:guid}")]
[Route("api/members/{studentId:guid}")]
public class StudentParentsController : ControllerBase
{
    private readonly ISender _mediator;

    public StudentParentsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("parents")]
    [HasPermission(Permissions.MembersView)]
    public async Task<IActionResult> List(Guid studentId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ListParentsQuery(studentId), cancellationToken));

    /// <summary>Links an existing account as the student's parent (max 2 per student).</summary>
    [HttpPost("parents")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> Link(Guid studentId, [FromBody] LinkParentRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new LinkParentCommand(studentId, request.ParentUserName), cancellationToken));

    [HttpDelete("parents/{parentId:guid}")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> Unlink(Guid studentId, Guid parentId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new UnlinkParentCommand(studentId, parentId), cancellationToken));

    /// <summary>One-time code (valid 14 days) the family uses to link a parent account themselves.</summary>
    [HttpPost("parent-invites")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> Invite(Guid studentId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new CreateParentInviteCommand(studentId), cancellationToken));

    public record LinkParentRequest(string ParentUserName);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>A parent's read-only view of one child in this organization.</summary>
[ApiController]
[Route("api/orgs/{org}/children")]
[Route("api/children")]
[HasPermission(Permissions.ChildrenView)]
public class ChildrenController : ControllerBase
{
    private readonly ISender _mediator;

    public ChildrenController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{childId:guid}")]
    public async Task<IActionResult> Overview(Guid childId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ChildOverviewQuery(childId), cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }
}
