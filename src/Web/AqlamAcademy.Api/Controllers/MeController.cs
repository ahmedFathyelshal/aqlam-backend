using System.Security.Claims;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
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

    public MeController(IMembershipService memberships)
    {
        _memberships = memberships;
    }

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
