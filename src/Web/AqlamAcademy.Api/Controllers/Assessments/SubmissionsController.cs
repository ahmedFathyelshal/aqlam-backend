using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Assessments;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Assessments;

/// <summary>Grading of assignment submissions (teachers, coordinators, admins).</summary>
[ApiController]
[Route("api/orgs/{org}/submissions")]
[Route("api/submissions")]
[HasPermission(Permissions.GradesEdit)]
public class SubmissionsController : ControllerBase
{
    private readonly ISender _mediator;

    public SubmissionsController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Grade the work, or return it for revision with feedback.</summary>
    [HttpPut("{id:guid}/grade")]
    public async Task<IActionResult> Grade(Guid id, [FromBody] GradeRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GradeSubmissionCommand(id, request.Score, request.Feedback, request.ReturnForRevision), cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }

    public record GradeRequest(decimal? Score, string? Feedback, bool ReturnForRevision = false);
}
