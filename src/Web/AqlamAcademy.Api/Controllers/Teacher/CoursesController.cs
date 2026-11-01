using AqlamAcademy.Application.Features.Teacher.Courses.Commands;
using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Teacher.Courses.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Teacher;

/// <summary>
/// Course authoring inside an organization. The organization comes from the {org} route value on the
/// main domain, or from the host on an organization's own (sub)domain.
/// </summary>
[Route("api/orgs/{org}/courses")]
[Route("api/courses")]
[ApiController]
[HasPermission(Permissions.CoursesEdit)]
public class CoursesController : ControllerBase
{
    private readonly IMediator _mediator;

    public CoursesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("draft")]
    public async Task<IActionResult> InitializeDraft([FromBody] InitializeCourseDraftCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        
        if (result.Succeeded)
            return Ok(result);
            
        return this.ToFailureResult(result);
    }

    [HttpGet("{id}/draft")]
    public async Task<IActionResult> GetDraft(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetCourseDraftQuery(id);
        var result = await _mediator.Send(query, cancellationToken);
        
        if (result.Succeeded)
            return Ok(result);
            
        return this.ToFailureResult(result);
    }

    [HttpPut("{id}/identity")]
    public async Task<IActionResult> UpdateIdentity(Guid id, [FromBody] UpdateCourseIdentityCommand command, CancellationToken cancellationToken)
    {
        if (id != command.Id) return BadRequest();

        var result = await _mediator.Send(command, cancellationToken);
        
        if (result.Succeeded)
            return Ok(result);
            
        return this.ToFailureResult(result);
    }

    [HttpPut("{id}/commercials")]
    public async Task<IActionResult> UpdateCommercials(Guid id, [FromBody] UpdateCourseCommercialsCommand command, CancellationToken cancellationToken)
    {
        if (id != command.Id) return BadRequest();

        var result = await _mediator.Send(command, cancellationToken);
        
        if (result.Succeeded)
            return Ok(result);
            
        return this.ToFailureResult(result);
    }

    [HttpPut("{id}/media")]
    public async Task<IActionResult> UpdateMedia(Guid id, [FromBody] UpdateCourseMediaCommand command, CancellationToken cancellationToken)
    {
        if (id != command.Id) return BadRequest();

        var result = await _mediator.Send(command, cancellationToken);
        
        if (result.Succeeded)
            return Ok(result);
            
        return this.ToFailureResult(result);
    }

    [HttpPut("{id}/curriculum")]
    public async Task<IActionResult> UpdateCurriculum(Guid id, [FromBody] UpdateCourseCurriculumCommand command, CancellationToken cancellationToken)
    {
        if (id != command.Id) return BadRequest();

        var result = await _mediator.Send(command, cancellationToken);
        
        if (result.Succeeded)
            return Ok(result);
            
        return this.ToFailureResult(result);
    }
}
