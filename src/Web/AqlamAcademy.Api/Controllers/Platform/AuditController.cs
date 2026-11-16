using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Platform;

/// <summary>Read-only audit trail for platform admins.</summary>
[ApiController]
[Route("api/platform/audit")]
[Authorize(Roles = PlatformRoles.SuperAdmin)]
public class AuditController(IAuditLog audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? action, [FromQuery] string? user, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(Result<IReadOnlyList<AuditEntryDto>>.Success(await audit.ListAsync(action, user, skip, take, cancellationToken)));
}
