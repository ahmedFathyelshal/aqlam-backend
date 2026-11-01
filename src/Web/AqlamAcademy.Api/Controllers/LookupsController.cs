using AqlamAcademy.Application.Features.Lookups.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class LookupsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LookupsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("curricula")]
    public async Task<IActionResult> GetCurricula([FromQuery] string? countryCode, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCurriculaQuery(countryCode), cancellationToken);

        return Ok(result);
    }

    [HttpGet("education-stages")]
    public async Task<IActionResult> GetEducationStages([FromQuery] int? curriculumId, CancellationToken cancellationToken)
    {
        var query = new GetEducationStagesQuery(curriculumId);
        var result = await _mediator.Send(query, cancellationToken);
        
        return Ok(result);
    }

    [HttpGet("education-grades")]
    public async Task<IActionResult> GetEducationGrades([FromQuery] int? stageId, CancellationToken cancellationToken)
    {
        var query = new GetEducationGradesQuery(stageId);
        var result = await _mediator.Send(query, cancellationToken);
        
        return Ok(result);
    }

    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects(CancellationToken cancellationToken)
    {
        var query = new GetSubjectsQuery();
        var result = await _mediator.Send(query, cancellationToken);
        
        return Ok(result);
    }

    [HttpGet("course-states")]
    public async Task<IActionResult> GetCourseStates(CancellationToken cancellationToken)
    {
        var query = new GetCourseStatesQuery();
        var result = await _mediator.Send(query, cancellationToken);
        
        return Ok(result);
    }
}
