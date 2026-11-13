using System.Security.Claims;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.DependencyInjection;
using AqlamAcademy.Application.Features.Organizations.Members;
using AqlamAcademy.Application.Features.Communication;
using AqlamAcademy.Application.Features.Parents;
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

    /// <summary>The user's notifications across organizations, newest first, with the unread count.</summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] bool unreadOnly, [FromQuery] int take = 30, CancellationToken cancellationToken = default) =>
        Ok(await _mediator.Send(new ListNotificationsQuery(unreadOnly, take), cancellationToken));

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new MarkNotificationsReadCommand(id), cancellationToken));

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new MarkNotificationsReadCommand(null), cancellationToken));

    /// <summary>Children linked to the signed-in parent, with the organizations where the parent can follow them.</summary>
    [HttpGet("children")]
    public async Task<IActionResult> Children(CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new MyChildrenQuery(), cancellationToken));

    /// <summary>Links the signed-in user as a parent with the code the school gave the family.</summary>
    [HttpPost("children")]
    [EnableRateLimiting(PresentationDependencyInjection.AuthRateLimitPolicy)] // slows down code guessing
    public async Task<IActionResult> LinkChild([FromBody] RedeemParentInviteCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(command, cancellationToken));

    /// <summary>A parent creates their child's account (required under 13) and is linked to it.</summary>
    [HttpPost("children/new")]
    [EnableRateLimiting(PresentationDependencyInjection.AuthRateLimitPolicy)]
    public async Task<IActionResult> CreateChild([FromBody] CreateChildAccountCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(command, cancellationToken));

    /// <summary>A student's family code for a parent who will pay or follow them (shows the open code if there is one).</summary>
    [HttpPost("family-invite")]
    public async Task<IActionResult> FamilyInvite(CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new CreateFamilyInviteCommand(), cancellationToken));

    public record BirthYearRequest(int BirthYear);

    /// <summary>Adds the birth year to an account that has none (accounts made before the age rules). Set once.</summary>
    [HttpPut("birth-year")]
    public async Task<IActionResult> SetBirthYear([FromBody] BirthYearRequest request, [FromServices] IIdentityService identity, [FromServices] ICurrentUserService currentUser)
    {
        if (!Domain.Common.AgeRules.IsValidBirthYear(request.BirthYear, DateTime.UtcNow))
            return BadRequest(Result.Failure(["Enter a valid birth year."]));
        return ToActionResult(await identity.SetBirthYearOnceAsync(currentUser.UserId, request.BirthYear));
    }

    private IActionResult ToActionResult(Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
