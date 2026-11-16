using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Platform;

/// <summary>Support tools: find a user, lock or unlock them, reset their password, suspend an organization.</summary>
[ApiController]
[Route("api/platform")]
[Authorize(Roles = PlatformRoles.SuperAdmin)]
public class PlatformSupportController(IPlatformAdminService admin, IPasswordRecoveryService recovery, ICurrentUserService me) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? q, [FromQuery] int take = 25, CancellationToken cancellationToken = default) =>
        Ok(Result<IReadOnlyList<PlatformUserDto>>.Success(await admin.SearchUsersAsync(q, take, cancellationToken)));

    [HttpPost("users/{userId:guid}/lock")]
    public async Task<IActionResult> Lock(Guid userId, CancellationToken cancellationToken) =>
        ToActionResult(await admin.SetUserLockedAsync(me.UserId, userId, true, cancellationToken));

    [HttpPost("users/{userId:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid userId, CancellationToken cancellationToken) =>
        ToActionResult(await admin.SetUserLockedAsync(me.UserId, userId, false, cancellationToken));

    [HttpPost("users/{userId:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid userId, CancellationToken cancellationToken)
    {
        var result = await recovery.ResetAnyPasswordAsync(me.UserId, userId, cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    [HttpPost("organizations/{slug}/suspend")]
    public async Task<IActionResult> Suspend(string slug, CancellationToken cancellationToken) =>
        ToActionResult(await admin.SetOrganizationSuspendedAsync(me.UserId, slug, true, cancellationToken));

    [HttpPost("organizations/{slug}/reactivate")]
    public async Task<IActionResult> Reactivate(string slug, CancellationToken cancellationToken) =>
        ToActionResult(await admin.SetOrganizationSuspendedAsync(me.UserId, slug, false, cancellationToken));

    private IActionResult ToActionResult(Result result) => result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
