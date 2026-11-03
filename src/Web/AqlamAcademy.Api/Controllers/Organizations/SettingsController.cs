using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Organizations.Settings;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Organizations;

/// <summary>Profile, branding and locale (time zone, Hijri/Gregorian, week start) of the current organization.</summary>
[ApiController]
[Route("api/orgs/{org}/settings")]
[Route("api/settings")]
[HasPermission(Permissions.OrganizationSettings)]
public class SettingsController : ControllerBase
{
    private readonly ISender _mediator;

    public SettingsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetOrganizationSettingsQuery(), cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateOrganizationSettingsCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(command, cancellationToken));

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
