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

    /// <summary>
    /// Saves the curriculum tree non-destructively (ids preserved, removed items soft-deleted). Send the
    /// curriculumVersion you loaded as expectedVersion; a stale version returns 409.
    /// </summary>
    [HttpPut("{id}/curriculum")]
    public async Task<IActionResult> SyncCurriculum(Guid id, [FromBody] SyncCurriculumRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SyncCurriculumCommand(id, request.ExpectedVersion, request.SyllabusRequirements, request.Chapters ?? []),
            cancellationToken);

        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    /// <summary>Courses of the current organization (newest activity first).</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListCoursesQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>What still blocks publishing (empty list = ready).</summary>
    [HttpGet("{id}/publish-checklist")]
    public async Task<IActionResult> PublishChecklist(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPublishChecklistQuery(id), cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    /// <summary>Draft / Scheduled / Published / Archived. Scheduling or publishing requires a complete course.</summary>
    [HttpPut("{id}/status")]
    [HasPermission(Permissions.CoursesPublish)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ChangeCourseStatusCommand(id, request.Status), cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    /// <summary>Private / Unlisted / Public. Public also requires the organization to allow public publishing.</summary>
    [HttpPut("{id}/visibility")]
    [HasPermission(Permissions.CoursesPublish)]
    public async Task<IActionResult> SetVisibility(Guid id, [FromBody] SetVisibilityRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetCourseVisibilityCommand(id, request.Visibility), cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    /// <summary>Students enrolled in the course (staff view).</summary>
    [HttpGet("{id}/enrollments")]
    [HasPermission(Permissions.MembersView)]
    public async Task<IActionResult> Enrollments(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AqlamAcademy.Application.Features.Learning.ListEnrollmentsQuery(id), cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    /// <summary>Enrolls existing members (by username) into the course; per-user results.</summary>
    [HttpPost("{id}/enrollments")]
    [HasPermission(Permissions.MembersManage)]
    public async Task<IActionResult> Enroll(Guid id, [FromBody] EnrollMembersRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AqlamAcademy.Application.Features.Learning.EnrollMembersCommand(id, request.UserNames), cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    public record EnrollMembersRequest(IReadOnlyList<string> UserNames);
    public record SyncCurriculumRequest(int? ExpectedVersion, string? SyllabusRequirements, IReadOnlyList<SyncChapterInput>? Chapters);
    public record ChangeStatusRequest(AqlamAcademy.Domain.Enums.CourseStatus Status);
    public record SetVisibilityRequest(AqlamAcademy.Domain.Enums.CourseVisibility Visibility);
}
