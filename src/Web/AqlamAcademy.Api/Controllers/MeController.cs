using System.Security.Claims;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.DependencyInjection;
using AqlamAcademy.Application.Features.Organizations.Members;
using MediatR;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers;

/// <summary>Platform-level endpoints about the signed-in user (no organization context).</summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IMembershipService _memberships;
    private readonly ISender _mediator;

    public MeController(IMembershipService memberships, ISender mediator)
    {
        _memberships = memberships;
        _mediator = mediator;
    }

    /// <summary>Joins an organization with a join code (e.g. printed on a center's QR poster).</summary>
    [HttpPost("join")]
    [EnableRateLimiting(PresentationDependencyInjection.AuthRateLimitPolicy)] // slows down code guessing
    public async Task<IActionResult> Join([FromBody] JoinOrganizationCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    /// <summary>The user's courses across all organizations, most recently used first.</summary>
    [HttpGet("courses")]
    public async Task<IActionResult> Courses(CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new AqlamAcademy.Application.Features.Learning.GetMyCoursesQuery(), cancellationToken));

    /// <summary>Organizations the user belongs to, with their roles (used for the organization switcher).</summary>
    [HttpGet("memberships")]
    public async Task<ActionResult<Result<IReadOnlyList<MembershipSummaryDto>>>> GetMemberships(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var memberships = await _memberships.GetUserMembershipsAsync(userId, cancellationToken);
        return Ok(Result<IReadOnlyList<MembershipSummaryDto>>.Success(memberships));
    }
}
