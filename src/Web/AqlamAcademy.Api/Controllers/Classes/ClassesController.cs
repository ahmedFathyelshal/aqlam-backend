using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Features.Classes;
using AqlamAcademy.Domain.Classes;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Controllers.Classes;

/// <summary>Branches / campuses of the current organization.</summary>
[ApiController]
[Route("api/orgs/{org}/branches")]
[Route("api/branches")]
public class BranchesController : ControllerBase
{
    private readonly ISender _mediator;

    public BranchesController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [HasPermission(Permissions.MembersView)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new ListBranchesQuery(), cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Create([FromBody] BranchRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveBranchCommand(null, request.Name, request.Gender, request.Address), cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] BranchRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveBranchCommand(id, request.Name, request.Gender, request.Address), cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new DeleteBranchCommand(id), cancellationToken));

    public record BranchRequest(string Name, GenderSegment Gender = GenderSegment.Mixed, string? Address = null);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>Classes (schools) and groups (centers): rosters and teacher ↔ subject assignments.</summary>
[ApiController]
[Route("api/orgs/{org}/classes")]
[Route("api/classes")]
public class ClassesController : ControllerBase
{
    private readonly ISender _mediator;

    public ClassesController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>All classes, or with mine=true those the current user teaches or is homeroom teacher of.</summary>
    [HttpGet]
    [HasPermission(Permissions.MembersView)]
    public async Task<IActionResult> List([FromQuery] bool mine, [FromQuery] Guid? branchId, [FromQuery] Guid? yearId, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new ListClassesQuery(mine, branchId, yearId), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MembersView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new GetClassQuery(id), cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Create([FromBody] ClassInput input, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveClassCommand(null, input), cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ClassInput input, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new SaveClassCommand(id, input), cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new DeleteClassCommand(id), cancellationToken));

    /// <summary>Adds students by username; returns a result per username. Students get the class's live courses.</summary>
    [HttpPost("{id:guid}/students")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> AddStudents(Guid id, [FromBody] AddStudentsRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new AddClassStudentsCommand(id, request.UserNames), cancellationToken));

    [HttpDelete("{id:guid}/students/{userId:guid}")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> RemoveStudent(Guid id, Guid userId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new RemoveClassStudentCommand(id, userId), cancellationToken));

    /// <summary>Assigns a teacher to a subject in this class and creates the class's course for that subject.</summary>
    [HttpPost("{id:guid}/assignments")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new AssignTeacherCommand(id, request.SubjectId, request.SubjectName, request.TeacherUserId, request.CurriculumId), cancellationToken));

    [HttpPut("{id:guid}/assignments/{assignmentId:guid}")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> ChangeTeacher(Guid id, Guid assignmentId, [FromBody] ChangeTeacherRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new ChangeAssignmentTeacherCommand(id, assignmentId, request.TeacherUserId), cancellationToken));

    [HttpDelete("{id:guid}/assignments/{assignmentId:guid}")]
    [HasPermission(Permissions.ClassesManage)]
    public async Task<IActionResult> RemoveAssignment(Guid id, Guid assignmentId, CancellationToken cancellationToken) =>
        ToActionResult(await _mediator.Send(new RemoveAssignmentCommand(id, assignmentId), cancellationToken));

    public record AddStudentsRequest(IReadOnlyList<string> UserNames);
    public record AssignRequest(int? SubjectId, string? SubjectName, Guid TeacherUserId, int? CurriculumId = null);
    public record ChangeTeacherRequest(Guid TeacherUserId);

    private IActionResult ToActionResult(Application.Common.Models.Result result) =>
        result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}
