using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Platform.Organizations;
using AqlamAcademy.Domain.Organizations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Platform;

/// <summary>Platform administration of organizations (centers and schools).</summary>
[ApiController]
[Route("api/platform/organizations")]
[Authorize(Roles = PlatformRoles.SuperAdmin)]
public class OrganizationsController : ControllerBase
{
    private readonly ISender _mediator;

    public OrganizationsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] OrganizationType? type, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrganizationsQuery(type), cancellationToken);
        return Ok(result);
    }
}
