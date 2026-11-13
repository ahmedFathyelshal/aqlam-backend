using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Marketplace;
using AqlamAcademy.Domain.Classes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Marketplace;

/// <summary>
/// The public marketplace (stories 3.5.1–3.5.4): open to visitors before sign-in. Only public courses of personal
/// academies and centers, and teachers who chose to be listed, are visible. Students are never searchable.
/// </summary>
[ApiController]
[Route("api/marketplace")]
[AllowAnonymous]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Infrastructure.DependencyInjection.PresentationDependencyInjection.PublicReadRateLimitPolicy)]
public class MarketplaceController(IMarketplaceReader reader) : ControllerBase
{
    [HttpGet("courses")]
    public async Task<IActionResult> Courses([FromQuery] string? q, [FromQuery] string? country, [FromQuery] int? curriculumId, [FromQuery] int? gradeId,
        [FromQuery] int? subjectId, [FromQuery] Gender? teacherGender, [FromQuery] bool? free, [FromQuery] int page = 1, [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default) =>
        Ok(Result<PagedDto<CourseCardDto>>.Success(await reader.SearchCoursesAsync(
            new MarketplaceSearch(q, country, curriculumId, gradeId, subjectId, teacherGender, free, page, pageSize), cancellationToken)));

    [HttpGet("teachers")]
    public async Task<IActionResult> Teachers([FromQuery] string? q, [FromQuery] string? country, [FromQuery] int? curriculumId, [FromQuery] int? gradeId,
        [FromQuery] int? subjectId, [FromQuery] Gender? teacherGender, [FromQuery] int page = 1, [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default) =>
        Ok(Result<PagedDto<TeacherCardDto>>.Success(await reader.SearchTeachersAsync(
            new MarketplaceSearch(q, country, curriculumId, gradeId, subjectId, teacherGender, null, page, pageSize), cancellationToken)));

    [HttpGet("facets")]
    public async Task<IActionResult> Facets([FromQuery] string? country, CancellationToken cancellationToken) =>
        Ok(Result<FacetsDto>.Success(await reader.FacetsAsync(country, cancellationToken)));

    /// <summary>Teachers picked by أقلام, optionally for one subject. Shown as picks, never as a ranking.</summary>
    [HttpGet("featured")]
    public async Task<IActionResult> Featured([FromQuery] int? subjectId, [FromQuery] string? country, CancellationToken cancellationToken) =>
        Ok(Result<IReadOnlyList<FeaturedTeacherDto>>.Success(await reader.FeaturedAsync(subjectId, country, cancellationToken)));

    [HttpGet("teachers/{slug}")]
    public async Task<IActionResult> Teacher(string slug, CancellationToken cancellationToken) =>
        ToActionResult(await reader.TeacherAsync(slug, cancellationToken));

    [HttpGet("teachers/{slug}/photo")]
    public async Task<IActionResult> Photo(string slug, CancellationToken cancellationToken)
    {
        var photo = await reader.TeacherPhotoAsync(slug, cancellationToken);
        if (photo is null)
            return NotFound();
        Response.Headers.CacheControl = "public, max-age=604800"; // the address carries a version
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(photo.Value.Content, photo.Value.ContentType);
    }

    [HttpGet("courses/{slug}")]
    public async Task<IActionResult> Course(string slug, CancellationToken cancellationToken)
    {
        // The viewer part depends on the signed-in user, so shared caches must not keep it.
        Response.Headers.CacheControl = "private, no-store";
        return ToActionResult(await reader.CourseAsync(slug, cancellationToken));
    }

    /// <summary>A lesson the teacher marked as a free preview.</summary>
    [HttpGet("courses/{slug}/lessons/{lessonId:guid}")]
    public async Task<IActionResult> Preview(string slug, Guid lessonId, CancellationToken cancellationToken) =>
        ToActionResult(await reader.PreviewAsync(slug, lessonId, cancellationToken));

    private IActionResult ToActionResult(Result result) => result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>An independent teacher's public page and "show me in search" switch.</summary>
[ApiController]
[Route("api/orgs/{org}/public-profile")]
[Route("api/public-profile")]
[HasPermission(Permissions.OrganizationSettings)]
public class PublicProfileController(IPublicProfileService profiles, ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => ToActionResult(await profiles.GetAsync(cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePublicProfileCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await mediator.Send(command, cancellationToken));

    /// <summary>PNG, JPEG or WebP, at most 256 KB.</summary>
    [HttpPut("photo")]
    [RequestSizeLimit(512 * 1024)]
    public async Task<IActionResult> UploadPhoto(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(Result.Failure(["Choose an image file."]));
        if (file.Length > Domain.Organizations.OrganizationLogo.MaxBytes)
            return BadRequest(Result.Failure(["The photo must be 256 KB or smaller."]));
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return ToActionResult(await mediator.Send(new SaveTeacherPhotoCommand(buffer.ToArray()), cancellationToken));
    }

    [HttpDelete("photo")]
    public async Task<IActionResult> RemovePhoto(CancellationToken cancellationToken) => ToActionResult(await profiles.RemovePhotoAsync(cancellationToken));

    private IActionResult ToActionResult(Result result) => result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>Platform admin: featured teachers, fee rates and agreed commissions.</summary>
[ApiController]
[Route("api/platform/marketplace")]
[Authorize(Roles = PlatformRoles.SuperAdmin)]
public class PlatformMarketplaceController(IPlatformMarketplaceService service, ISender mediator) : ControllerBase
{
    [HttpGet("featured")]
    public async Task<IActionResult> Featured(CancellationToken cancellationToken) =>
        Ok(Result<IReadOnlyList<FeaturedTeacherDto>>.Success(await service.ListFeaturedAsync(cancellationToken)));

    /// <summary>Listed teachers the admin can pick from.</summary>
    [HttpGet("teachers")]
    public async Task<IActionResult> Teachers([FromQuery] string? q, CancellationToken cancellationToken) =>
        Ok(Result<IReadOnlyList<AdminTeacherDto>>.Success(await service.ListedTeachersAsync(q, cancellationToken)));

    [HttpPost("featured")]
    public async Task<IActionResult> Feature([FromBody] FeatureTeacherCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await mediator.Send(command, cancellationToken));

    [HttpDelete("featured/{id:guid}")]
    public async Task<IActionResult> Unfeature(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await service.UnfeatureAsync(id, cancellationToken));

    [HttpGet("fees")]
    public async Task<IActionResult> Fees(CancellationToken cancellationToken) =>
        Ok(Result<FeesDto>.Success(await service.GetFeesAsync(cancellationToken)));

    [HttpPut("fees")]
    public async Task<IActionResult> SaveFees([FromBody] SaveFeesCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await mediator.Send(command, cancellationToken));

    public record CommissionRequest(decimal? CommissionPercent);

    /// <summary>Commission agreed with one organization; null goes back to the platform rate.</summary>
    [HttpPut("organizations/{slug}/commission")]
    public async Task<IActionResult> Commission(string slug, [FromBody] CommissionRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await mediator.Send(new SetCommissionCommand(slug, request.CommissionPercent), cancellationToken));

    private IActionResult ToActionResult(Result result) => result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
