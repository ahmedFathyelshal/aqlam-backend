using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Attendance;
using AqlamAcademy.Application.Features.Communication;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Classes;

/// <summary>Attendance sheets of a class: daily (startTime omitted) or per group meeting (startTime set).</summary>
[ApiController]
[Route("api/orgs/{org}/classes/{classId:guid}/attendance")]
[Route("api/classes/{classId:guid}/attendance")]
[HasPermission(Permissions.AttendanceTake)]
public class AttendanceController : ControllerBase
{
    private readonly ISender _mediator;

    public AttendanceController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid classId, [FromQuery] DateOnly date, [FromQuery] TimeOnly? startTime, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetAttendanceSheetQuery(classId, date, startTime), cancellationToken));

    /// <summary>Saves marks for the sheet (idempotent: resending the same sheet updates it). Absent/late alerts go to parents.</summary>
    [HttpPut]
    public async Task<IActionResult> Save(Guid classId, [FromBody] SaveAttendanceRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveAttendanceCommand(classId, request.Date, request.StartTime, request.Marks ?? []), cancellationToken));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(Guid classId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetAttendanceSummaryQuery(classId, from, to), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid classId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ExportAttendanceQuery(classId, from, to), cancellationToken);
        return result.Succeeded
            ? File(result.Data!.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Data.FileName)
            : this.ToFailureResult(result);
    }

    public record SaveAttendanceRequest(DateOnly Date, TimeOnly? StartTime, IReadOnlyList<AttendanceMark>? Marks);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>Announcements of the current organization.</summary>
[ApiController]
[Route("api/orgs/{org}/announcements")]
[Route("api/announcements")]
[HasPermission(Permissions.AnnouncementsSend)]
public class AnnouncementsController : ControllerBase
{
    private readonly ISender _mediator;

    public AnnouncementsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? classId, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new ListAnnouncementsQuery(classId), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Publish([FromBody] PublishAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }
}
