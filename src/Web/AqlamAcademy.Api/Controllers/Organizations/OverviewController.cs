using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Organizations.Overview;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Organizations;

/// <summary>The owner's morning view: today's attendance, classes, students to follow up and homework to grade.</summary>
[ApiController]
[Route("api/orgs/{org}/overview")]
[Route("api/overview")]
[HasPermission(Permissions.ClassesManage)]
public class OverviewController : ControllerBase
{
    private readonly ISender _mediator;

    public OverviewController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrganizationOverviewQuery(), cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }
}

/// <summary>A teacher's day: their own classes' attendance today and homework waiting in their courses.</summary>
[ApiController]
[Route("api/orgs/{org}/my-day")]
[Route("api/my-day")]
[HasPermission(Permissions.AttendanceTake)]
public class MyDayController : ControllerBase
{
    private readonly ISender _mediator;

    public MyDayController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrganizationOverviewQuery(Mine: true), cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }
}
