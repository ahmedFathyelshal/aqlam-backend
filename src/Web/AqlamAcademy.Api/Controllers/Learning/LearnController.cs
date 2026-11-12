using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Application.Features.Learning;
using AqlamAcademy.Application.Features.Assessments;
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

    /// <summary>The signed-in student's next lessons, work due and latest result in this organization.</summary>
    [HttpGet("home")]
    public async Task<IActionResult> Home(CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetLearnerHomeQuery(), cancellationToken));

    [HttpGet("courses/{courseId:guid}")]
    public async Task<IActionResult> Course(Guid courseId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetLearnerCourseQuery(courseId), cancellationToken));

    [HttpPost("courses/{courseId:guid}/enroll")]
    public async Task<IActionResult> Enroll(Guid courseId, [FromQuery] Guid? forUserId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new EnrollSelfCommand(courseId, forUserId), cancellationToken));

    /// <summary>Starts paying for a paid course; the response says where to send the student.</summary>
    [HttpPost("courses/{courseId:guid}/checkout")]
    public async Task<IActionResult> Checkout(Guid courseId, [FromQuery] Guid? forUserId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new Application.Features.Payments.CheckoutCommand(courseId, forUserId), cancellationToken));

    [HttpGet("lessons/{lessonId:guid}")]
    public async Task<IActionResult> Lesson(Guid lessonId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetLessonQuery(lessonId), cancellationToken));

    /// <summary>Player heartbeat (every 10–15 s while watching) or "mark as done" for articles.</summary>
    [HttpPost("lessons/{lessonId:guid}/progress")]
    public async Task<IActionResult> Progress(Guid lessonId, [FromBody] ProgressRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(
            new RecordProgressCommand(lessonId, request.PositionSeconds, request.WatchedPercent, request.MarkCompleted), cancellationToken));

    /// <summary>Quiz status for the student: attempts used, best score, running attempt.</summary>
    [HttpGet("lessons/{lessonId:guid}/quiz")]
    public async Task<IActionResult> Quiz(Guid lessonId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetQuizSummaryQuery(lessonId), cancellationToken));

    /// <summary>Starts an attempt (or resumes the running one).</summary>
    [HttpPost("lessons/{lessonId:guid}/quiz/attempts")]
    public async Task<IActionResult> StartAttempt(Guid lessonId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new StartAttemptCommand(lessonId), cancellationToken));

    /// <summary>Autosave while answering.</summary>
    [HttpPut("attempts/{attemptId:guid}/answers")]
    public async Task<IActionResult> SaveAnswers(Guid attemptId, [FromBody] AnswersRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveAnswersCommand(attemptId, request.Answers ?? []), cancellationToken));

    [HttpPost("attempts/{attemptId:guid}/submit")]
    public async Task<IActionResult> SubmitAttempt(Guid attemptId, [FromBody] AnswersRequest? request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SubmitAttemptCommand(attemptId, request?.Answers), cancellationToken));

    [HttpGet("lessons/{lessonId:guid}/assignment")]
    public async Task<IActionResult> Assignment(Guid lessonId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetStudentAssignmentQuery(lessonId), cancellationToken));

    /// <summary>Hand in (or revise) the assignment; completes the lesson.</summary>
    [HttpPut("lessons/{lessonId:guid}/assignment/submission")]
    public async Task<IActionResult> Submit(Guid lessonId, [FromBody] SubmissionRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SubmitAssignmentCommand(lessonId, request.Text, request.Links), cancellationToken));

    public record SubmissionRequest(string? Text, IReadOnlyList<string>? Links);

    public record AnswersRequest(IReadOnlyList<AnswerInput>? Answers);

    public record ProgressRequest(int? PositionSeconds, int? WatchedPercent, bool MarkCompleted = false);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
