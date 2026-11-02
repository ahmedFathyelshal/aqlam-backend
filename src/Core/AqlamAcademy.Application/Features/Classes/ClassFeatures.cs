using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Classes;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Classes;

public record BranchDto(Guid Id, string Name, string Gender, string? Address);

public record WeeklySlotDto(DayOfWeek Day, TimeOnly StartTime, TimeOnly EndTime);

public record ClassSummaryDto(
    Guid Id,
    string Name,
    string Kind,
    string Gender,
    Guid? BranchId,
    string? BranchName,
    int? CurriculumId,
    int? GradeId,
    Guid? HomeroomTeacherUserId,
    string? HomeroomTeacherName,
    int? Capacity,
    int StudentCount,
    IReadOnlyList<WeeklySlotDto> Schedule);

public record ClassStudentDto(Guid UserId, string UserName, string FullName, string? Gender, DateTime AddedAtUtc);

public record TeachingAssignmentDto(
    Guid Id,
    int? SubjectId,
    string SubjectName,
    Guid TeacherUserId,
    string? TeacherUserName,
    string? TeacherName,
    Guid CourseId,
    string? CourseStatus);

public record ClassDetailDto(ClassSummaryDto Class, IReadOnlyList<ClassStudentDto> Students, IReadOnlyList<TeachingAssignmentDto> Assignments);

public record RosterResultDto(string UserName, string Status, string? Error);

/// <summary>Input shared by create and update.</summary>
public record ClassInput(
    string Name,
    ClassKind Kind,
    GenderSegment Gender,
    Guid? BranchId,
    int? CurriculumId,
    int? GradeId,
    Guid? HomeroomTeacherUserId,
    int? Capacity,
    IReadOnlyList<WeeklySlotDto>? Schedule);

/// <summary>Branches, classes/groups, rosters and teacher assignments of the current organization.</summary>
public interface IClassService
{
    Task<IReadOnlyList<BranchDto>> ListBranchesAsync(CancellationToken cancellationToken);
    Task<Result<BranchDto>> SaveBranchAsync(Guid? id, string name, GenderSegment gender, string? address, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteBranchAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>mine = classes where the current user is homeroom teacher or teaches a subject.</summary>
    Task<IReadOnlyList<ClassSummaryDto>> ListClassesAsync(bool mine, Guid? branchId, CancellationToken cancellationToken);
    Task<Result<ClassDetailDto>> GetClassAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ClassSummaryDto>> SaveClassAsync(Guid? id, ClassInput input, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteClassAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<RosterResultDto>>> AddStudentsAsync(Guid classId, IReadOnlyList<string> userNames, CancellationToken cancellationToken);
    Task<Result<bool>> RemoveStudentAsync(Guid classId, Guid userId, CancellationToken cancellationToken);

    Task<Result<TeachingAssignmentDto>> AssignTeacherAsync(Guid classId, int? subjectId, string? subjectName, Guid teacherUserId, CancellationToken cancellationToken);
    Task<Result<TeachingAssignmentDto>> ChangeTeacherAsync(Guid classId, Guid assignmentId, Guid teacherUserId, CancellationToken cancellationToken);
    Task<Result<bool>> RemoveAssignmentAsync(Guid classId, Guid assignmentId, CancellationToken cancellationToken);
}

// ---- Branches -----------------------------------------------------------------------------------------------

public record ListBranchesQuery : IRequest<Result<IReadOnlyList<BranchDto>>>;

public class ListBranchesQueryHandler(IClassService classes) : IRequestHandler<ListBranchesQuery, Result<IReadOnlyList<BranchDto>>>
{
    public async Task<Result<IReadOnlyList<BranchDto>>> Handle(ListBranchesQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<BranchDto>>.Success(await classes.ListBranchesAsync(cancellationToken));
}

public record SaveBranchCommand(Guid? Id, string Name, GenderSegment Gender, string? Address) : IRequest<Result<BranchDto>>;

public class SaveBranchCommandValidator : AbstractValidator<SaveBranchCommand>
{
    public SaveBranchCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.Address).MaximumLength(500);
    }
}

public class SaveBranchCommandHandler(IClassService classes) : IRequestHandler<SaveBranchCommand, Result<BranchDto>>
{
    public Task<Result<BranchDto>> Handle(SaveBranchCommand r, CancellationToken cancellationToken) =>
        classes.SaveBranchAsync(r.Id, r.Name.Trim(), r.Gender, r.Address?.Trim(), cancellationToken);
}

public record DeleteBranchCommand(Guid Id) : IRequest<Result<bool>>;

public class DeleteBranchCommandHandler(IClassService classes) : IRequestHandler<DeleteBranchCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(DeleteBranchCommand request, CancellationToken cancellationToken) =>
        classes.DeleteBranchAsync(request.Id, cancellationToken);
}

// ---- Classes ------------------------------------------------------------------------------------------------

public record ListClassesQuery(bool Mine, Guid? BranchId) : IRequest<Result<IReadOnlyList<ClassSummaryDto>>>;

public class ListClassesQueryHandler(IClassService classes) : IRequestHandler<ListClassesQuery, Result<IReadOnlyList<ClassSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<ClassSummaryDto>>> Handle(ListClassesQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<ClassSummaryDto>>.Success(await classes.ListClassesAsync(request.Mine, request.BranchId, cancellationToken));
}

public record GetClassQuery(Guid Id) : IRequest<Result<ClassDetailDto>>;

public class GetClassQueryHandler(IClassService classes) : IRequestHandler<GetClassQuery, Result<ClassDetailDto>>
{
    public Task<Result<ClassDetailDto>> Handle(GetClassQuery request, CancellationToken cancellationToken) =>
        classes.GetClassAsync(request.Id, cancellationToken);
}

public record SaveClassCommand(Guid? Id, ClassInput Input) : IRequest<Result<ClassSummaryDto>>;

public class SaveClassCommandValidator : AbstractValidator<SaveClassCommand>
{
    public SaveClassCommandValidator()
    {
        RuleFor(x => x.Input.Name).NotEmpty().MaximumLength(100).OverridePropertyName("name");
        RuleFor(x => x.Input.Kind).IsInEnum().OverridePropertyName("kind");
        RuleFor(x => x.Input.Gender).IsInEnum().OverridePropertyName("gender");
        RuleFor(x => x.Input.Capacity).InclusiveBetween(1, 1000).When(x => x.Input.Capacity is not null).OverridePropertyName("capacity");
        RuleFor(x => x.Input.Schedule).Must(s => s is null || s.Count <= 14).WithMessage("At most 14 weekly slots.").OverridePropertyName("schedule");
        RuleForEach(x => x.Input.Schedule).Must(s => s.EndTime > s.StartTime)
            .WithMessage("Each slot must end after it starts.").OverridePropertyName("schedule");
    }
}

public class SaveClassCommandHandler(IClassService classes) : IRequestHandler<SaveClassCommand, Result<ClassSummaryDto>>
{
    public Task<Result<ClassSummaryDto>> Handle(SaveClassCommand request, CancellationToken cancellationToken) =>
        classes.SaveClassAsync(request.Id, request.Input with { Name = request.Input.Name.Trim() }, cancellationToken);
}

public record DeleteClassCommand(Guid Id) : IRequest<Result<bool>>;

public class DeleteClassCommandHandler(IClassService classes) : IRequestHandler<DeleteClassCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(DeleteClassCommand request, CancellationToken cancellationToken) =>
        classes.DeleteClassAsync(request.Id, cancellationToken);
}

// ---- Roster -------------------------------------------------------------------------------------------------

public record AddClassStudentsCommand(Guid ClassId, IReadOnlyList<string> UserNames) : IRequest<Result<IReadOnlyList<RosterResultDto>>>;

public class AddClassStudentsCommandValidator : AbstractValidator<AddClassStudentsCommand>
{
    public AddClassStudentsCommandValidator()
    {
        RuleFor(x => x.UserNames).NotEmpty().Must(u => u.Count <= 500).WithMessage("At most 500 students at a time.");
    }
}

public class AddClassStudentsCommandHandler(IClassService classes) : IRequestHandler<AddClassStudentsCommand, Result<IReadOnlyList<RosterResultDto>>>
{
    public Task<Result<IReadOnlyList<RosterResultDto>>> Handle(AddClassStudentsCommand request, CancellationToken cancellationToken) =>
        classes.AddStudentsAsync(request.ClassId, request.UserNames, cancellationToken);
}

public record RemoveClassStudentCommand(Guid ClassId, Guid UserId) : IRequest<Result<bool>>;

public class RemoveClassStudentCommandHandler(IClassService classes) : IRequestHandler<RemoveClassStudentCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(RemoveClassStudentCommand request, CancellationToken cancellationToken) =>
        classes.RemoveStudentAsync(request.ClassId, request.UserId, cancellationToken);
}

// ---- Teacher assignments ------------------------------------------------------------------------------------

public record AssignTeacherCommand(Guid ClassId, int? SubjectId, string? SubjectName, Guid TeacherUserId) : IRequest<Result<TeachingAssignmentDto>>;

public class AssignTeacherCommandValidator : AbstractValidator<AssignTeacherCommand>
{
    public AssignTeacherCommandValidator()
    {
        RuleFor(x => x.TeacherUserId).NotEmpty();
        RuleFor(x => x.SubjectName).MaximumLength(100);
        RuleFor(x => x).Must(x => x.SubjectId is not null || !string.IsNullOrWhiteSpace(x.SubjectName))
            .WithName("subjectId").WithMessage("Choose a subject or type a custom subject name.");
    }
}

public class AssignTeacherCommandHandler(IClassService classes) : IRequestHandler<AssignTeacherCommand, Result<TeachingAssignmentDto>>
{
    public Task<Result<TeachingAssignmentDto>> Handle(AssignTeacherCommand r, CancellationToken cancellationToken) =>
        classes.AssignTeacherAsync(r.ClassId, r.SubjectId, r.SubjectName?.Trim(), r.TeacherUserId, cancellationToken);
}

public record ChangeAssignmentTeacherCommand(Guid ClassId, Guid AssignmentId, Guid TeacherUserId) : IRequest<Result<TeachingAssignmentDto>>;

public class ChangeAssignmentTeacherCommandHandler(IClassService classes) : IRequestHandler<ChangeAssignmentTeacherCommand, Result<TeachingAssignmentDto>>
{
    public Task<Result<TeachingAssignmentDto>> Handle(ChangeAssignmentTeacherCommand r, CancellationToken cancellationToken) =>
        classes.ChangeTeacherAsync(r.ClassId, r.AssignmentId, r.TeacherUserId, cancellationToken);
}

public record RemoveAssignmentCommand(Guid ClassId, Guid AssignmentId) : IRequest<Result<bool>>;

public class RemoveAssignmentCommandHandler(IClassService classes) : IRequestHandler<RemoveAssignmentCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(RemoveAssignmentCommand r, CancellationToken cancellationToken) =>
        classes.RemoveAssignmentAsync(r.ClassId, r.AssignmentId, cancellationToken);
}
