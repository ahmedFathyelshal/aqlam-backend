using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Assessments;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Assessments;

/// <summary>The organization's question bank (shared by all its teachers).</summary>
[ApiController]
[Route("api/orgs/{org}/questions")]
[Route("api/questions")]
[HasPermission(Permissions.CoursesEdit)]
public class QuestionsController : ControllerBase
{
    private readonly ISender _mediator;

    public QuestionsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? subjectId, [FromQuery] int? gradeId, [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new ListQuestionsQuery(subjectId, gradeId, search), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveQuestionCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(command with { Id = null }, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveQuestionCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(command with { Id = id }, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new DeleteQuestionCommand(id), cancellationToken));

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
