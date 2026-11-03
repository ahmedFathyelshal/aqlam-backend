using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Academics;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Academics;

/// <summary>Academic years and terms of the current organization.</summary>
[ApiController]
[Route("api/orgs/{org}/academic-years")]
[Route("api/academic-years")]
public class AcademicYearsController : ControllerBase
{
    private readonly ISender _mediator;

    public AcademicYearsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [HasPermission(Permissions.MembersView)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new ListAcademicYearsQuery(), cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Create([FromBody] SaveAcademicYearCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(command with { Id = null }, cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveAcademicYearCommand command, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(command with { Id = id }, cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new DeleteAcademicYearCommand(id), cancellationToken));

    [HttpPost("{id:guid}/current")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> SetCurrent(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SetCurrentAcademicYearCommand(id), cancellationToken));

    /// <summary>Publishing a term's report cards shows them to students and parents.</summary>
    [HttpPut("terms/{termId:guid}/report-cards")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> PublishReportCards(Guid termId, [FromBody] PublishRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SetReportCardsPublishedCommand(termId, request.Published), cancellationToken));

    public record PublishRequest(bool Published);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>Gradebook of one subject in a class. Teachers reach the subjects they teach; class managers reach all.</summary>
[ApiController]
[Route("api/orgs/{org}/classes/{classId:guid}/assignments/{assignmentId:guid}/gradebook")]
[Route("api/classes/{classId:guid}/assignments/{assignmentId:guid}/gradebook")]
[HasPermission(Permissions.GradesEdit)]
public class GradebookController : ControllerBase
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly ISender _mediator;

    public GradebookController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid classId, Guid assignmentId, [FromQuery] Guid termId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetGradebookQuery(classId, assignmentId, termId), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid classId, Guid assignmentId, [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ExportGradebookQuery(classId, assignmentId, termId), cancellationToken);
        return result.Succeeded ? File(result.Data!.Content, ExcelType, result.Data.FileName) : this.ToFailureResult(result);
    }

    [HttpPost("categories")]
    public async Task<IActionResult> AddCategory(Guid classId, Guid assignmentId, [FromBody] CategoryRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveGradeCategoryCommand(classId, assignmentId, null, request.TermId, request.Name, request.WeightPercent), cancellationToken));

    [HttpPut("categories/{categoryId:guid}")]
    public async Task<IActionResult> UpdateCategory(Guid classId, Guid assignmentId, Guid categoryId, [FromBody] CategoryRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveGradeCategoryCommand(classId, assignmentId, categoryId, request.TermId, request.Name, request.WeightPercent), cancellationToken));

    [HttpDelete("categories/{categoryId:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid classId, Guid assignmentId, Guid categoryId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new DeleteGradeCategoryCommand(classId, assignmentId, categoryId), cancellationToken));

    [HttpPost("categories/{categoryId:guid}/items")]
    public async Task<IActionResult> AddItem(Guid classId, Guid assignmentId, Guid categoryId, [FromBody] ItemRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveGradeItemCommand(classId, assignmentId, categoryId, null, request.Title, request.MaxPoints, request.SourceLessonId), cancellationToken));

    [HttpPut("categories/{categoryId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> UpdateItem(Guid classId, Guid assignmentId, Guid categoryId, Guid itemId, [FromBody] ItemRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveGradeItemCommand(classId, assignmentId, categoryId, itemId, request.Title, request.MaxPoints, request.SourceLessonId), cancellationToken));

    [HttpDelete("items/{itemId:guid}")]
    public async Task<IActionResult> DeleteItem(Guid classId, Guid assignmentId, Guid itemId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new DeleteGradeItemCommand(classId, assignmentId, itemId), cancellationToken));

    /// <summary>Saves typed scores; a null score clears it (a linked item goes back to the lesson's score).</summary>
    [HttpPut("items/{itemId:guid}/scores")]
    public async Task<IActionResult> SaveScores(Guid classId, Guid assignmentId, Guid itemId, [FromBody] ScoresRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveGradeScoresCommand(classId, assignmentId, itemId, request.Scores ?? []), cancellationToken));

    public record CategoryRequest(Guid TermId, string Name, decimal WeightPercent);
    public record ItemRequest(string Title, decimal MaxPoints, Guid? SourceLessonId);
    public record ScoresRequest(IReadOnlyList<GradeScoreInput>? Scores);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>Report cards: staff see any term; students and linked parents see published terms.</summary>
[ApiController]
[Route("api/orgs/{org}/report-cards")]
[Route("api/report-cards")]
public class ReportCardsController : ControllerBase
{
    private readonly ISender _mediator;

    public ReportCardsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("students/{studentId:guid}/terms/{termId:guid}/staff")]
    [HasPermission(Permissions.GradesEdit)]
    public async Task<IActionResult> ForStaff(Guid studentId, Guid termId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetStaffReportCardQuery(studentId, termId), cancellationToken));

    [HttpGet("students/{studentId:guid}/terms/{termId:guid}")]
    [Authorize]
    public async Task<IActionResult> ForFamily(Guid studentId, Guid termId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetFamilyReportCardQuery(studentId, termId), cancellationToken));

    [HttpGet("students/{studentId:guid}/terms")]
    [Authorize]
    public async Task<IActionResult> PublishedTerms(Guid studentId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetPublishedTermsQuery(studentId), cancellationToken));

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
