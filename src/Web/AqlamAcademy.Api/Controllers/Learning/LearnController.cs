using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Application.Features.Learning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Learning;

/// <summary>
/// Student side of a course inside an organization: outline with lock state, lesson content, progress
/// heartbeats and self-enrollment. Access is decided per request from the enrollment (staff get a preview).
/// </summary>
[ApiController]
[Route("api/orgs/{org}/learn")]
[Route("api/learn")]
[Authorize]
public class LearnController : ControllerBase
{
    private readonly ISender _mediator;

    public LearnController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("courses/{courseId:guid}")]
    public async Task<IActionResult> Course(Guid courseId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetLearnerCourseQuery(courseId), cancellationToken));

    [HttpPost("courses/{courseId:guid}/enroll")]
    public async Task<IActionResult> Enroll(Guid courseId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new EnrollSelfCommand(courseId), cancellationToken));

    [HttpGet("lessons/{lessonId:guid}")]
    public async Task<IActionResult> Lesson(Guid lessonId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetLessonQuery(lessonId), cancellationToken));

    /// <summary>Player heartbeat (every 10–15 s while watching) or "mark as done" for articles.</summary>
    [HttpPost("lessons/{lessonId:guid}/progress")]
    public async Task<IActionResult> Progress(Guid lessonId, [FromBody] ProgressRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(
            new RecordProgressCommand(lessonId, request.PositionSeconds, request.WatchedPercent, request.MarkCompleted), cancellationToken));

    public record ProgressRequest(int? PositionSeconds, int? WatchedPercent, bool MarkCompleted = false);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
