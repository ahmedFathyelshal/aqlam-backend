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

    /// <summary>Uploads the logo (PNG, JPEG or WebP, at most 256 KB).</summary>
    [HttpPut("logo")]
    [RequestSizeLimit(512 * 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(Application.Common.Models.Result.Failure(new[] { "Choose an image file." }));
        if (file.Length > Domain.Organizations.OrganizationLogo.MaxBytes)
            return BadRequest(Application.Common.Models.Result.Failure(new[] { "The logo must be 256 KB or smaller." }));

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return ToActionResult(await _mediator.Send(new SaveOrganizationLogoCommand(buffer.ToArray()), cancellationToken));
    }

    [HttpDelete("logo")]
    public async Task<IActionResult> RemoveLogo(CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new RemoveOrganizationLogoCommand(), cancellationToken));

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>A school's uploaded logo, public so sign-in pages, parents' phones and emails can show it.</summary>
[ApiController]
[Route("api/organizations/{slug}/logo")]
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public class OrganizationLogoController(IOrganizationSettingsService settings) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string slug, CancellationToken cancellationToken)
    {
        var logo = await settings.GetLogoAsync(slug, cancellationToken);
        if (logo is null)
            return NotFound();
        // The address carries a version, so it can be cached for long.
        Response.Headers.CacheControl = "public, max-age=604800";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(logo.Content, logo.ContentType);
    }
}
