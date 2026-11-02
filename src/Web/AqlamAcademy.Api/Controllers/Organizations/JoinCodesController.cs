using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Organizations.Members;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Organizations;

/// <summary>Join codes / QR posters that let students join the current organization.</summary>
[ApiController]
[Route("api/orgs/{org}/join-codes")]
[Route("api/join-codes")]
[HasPermission(Permissions.MembersManage)]
public class JoinCodesController : ControllerBase
{
    private readonly ISender _mediator;

    public JoinCodesController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ListJoinCodesQuery(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJoinCodeCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(command, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new DeactivateJoinCodeCommand(id), cancellationToken));

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
