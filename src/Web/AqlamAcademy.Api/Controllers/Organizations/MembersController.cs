using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Interfaces;
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

    /// <summary>Excel template for bulk import (columns: FullName, Role, Phone, Email).</summary>
    [HttpGet("import/template")]
    [HasPermission(Permissions.MembersManage)]
    public IActionResult ImportTemplate([FromServices] IMemberImportService importer) =>
        File(importer.BuildTemplate(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "members-import-template.xlsx");

    /// <summary>Creates accounts and memberships from an Excel sheet; returns a per-row report with temporary passwords.</summary>
    [HttpPost("import")]
    [HasPermission(Permissions.MembersManage)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(Application.Common.Models.Result.Failure(new[] { "Please upload an .xlsx file." }));

        await using var stream = file.OpenReadStream();
        return ToActionResult(await _mediator.Send(new ImportMembersCommand(stream), cancellationToken));
    }

    [HttpPut("{userId:guid}/roles")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> UpdateRoles(Guid userId, [FromBody] UpdateRolesRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new UpdateMemberRolesCommand(userId, request.Roles), cancellationToken));

    [HttpPut("{userId:guid}/gender")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> SetGender(Guid userId, [FromBody] SetGenderRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SetMemberGenderCommand(userId, request.Gender), cancellationToken));

    [HttpPost("{userId:guid}/approve")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> Approve(Guid userId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ApproveMemberCommand(userId), cancellationToken));

    [HttpDelete("{userId:guid}")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new RemoveMemberCommand(userId), cancellationToken));

    /// <summary>Sets a temporary password for a member (they must change it at next sign-in). Shown once.</summary>
    [HttpPost("{userId:guid}/reset-password")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> ResetPassword(Guid userId, [FromServices] IPasswordRecoveryService recovery, [FromServices] ICurrentUserService currentUser, [FromServices] ICurrentOrganization organization, CancellationToken cancellationToken)
    {
        if (organization.OrganizationId is not { } orgId)
            return NotFound();
        var result = await recovery.ResetMemberPasswordAsync(orgId, currentUser.UserId, userId, cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    public record UpdateRolesRequest(IReadOnlyList<string> Roles);
    public record SetGenderRequest(AqlamAcademy.Domain.Classes.Gender? Gender);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
