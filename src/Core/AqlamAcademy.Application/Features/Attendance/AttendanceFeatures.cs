using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Attendance;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Attendance;

/// <summary>One roster line of an attendance sheet; Status is null while not yet taken.</summary>
public record AttendanceLineDto(Guid UserId, string UserName, string FullName, string? Status, string? Note);

public record AttendanceSheetDto(
    Guid ClassGroupId,
    string ClassName,
    DateOnly Date,
    TimeOnly? StartTime,
    bool IsTaken,
    DateTime? TakenAtUtc,
    string? TakenByName,
    IReadOnlyList<AttendanceLineDto> Lines);

public record AttendanceSummaryRowDto(Guid UserId, string UserName, string FullName, int Present, int Absent, int Late, int Excused);

public record AttendanceEntryDto(DateOnly Date, TimeOnly? StartTime, Guid ClassGroupId, string ClassName, string Status, string? Note);

public record AttendanceMark(Guid UserId, AttendanceStatus Status, string? Note);

public interface IAttendanceService
{
    Task<Result<AttendanceSheetDto>> GetSheetAsync(Guid classId, DateOnly date, TimeOnly? startTime, CancellationToken cancellationToken);
    Task<Result<AttendanceSheetDto>> SaveSheetAsync(Guid classId, DateOnly date, TimeOnly? startTime, IReadOnlyList<AttendanceMark> marks, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<AttendanceSummaryRowDto>>> SummaryAsync(Guid classId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>A student's own record in the current organization (used by the student and by parents).</summary>
    Task<IReadOnlyList<AttendanceEntryDto>> HistoryOfAsync(Guid studentUserId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public record GetAttendanceSheetQuery(Guid ClassId, DateOnly Date, TimeOnly? StartTime) : IRequest<Result<AttendanceSheetDto>>;

public class GetAttendanceSheetQueryHandler(IAttendanceService attendance) : IRequestHandler<GetAttendanceSheetQuery, Result<AttendanceSheetDto>>
{
    public Task<Result<AttendanceSheetDto>> Handle(GetAttendanceSheetQuery r, CancellationToken cancellationToken) =>
        attendance.GetSheetAsync(r.ClassId, r.Date, r.StartTime, cancellationToken);
}

public record SaveAttendanceCommand(Guid ClassId, DateOnly Date, TimeOnly? StartTime, IReadOnlyList<AttendanceMark> Marks) : IRequest<Result<AttendanceSheetDto>>;

public class SaveAttendanceCommandValidator : AbstractValidator<SaveAttendanceCommand>
{
    public SaveAttendanceCommandValidator()
    {
        RuleFor(x => x.Marks).NotEmpty().Must(m => m.Count <= 1000);
        RuleFor(x => x.Marks).Must(m => m.Select(x => x.UserId).Distinct().Count() == m.Count)
            .WithMessage("Each student can only be marked once.");
        RuleForEach(x => x.Marks).ChildRules(mark =>
        {
            mark.RuleFor(m => m.Status).IsInEnum();
            mark.RuleFor(m => m.Note).MaximumLength(500);
        });
    }
}

public class SaveAttendanceCommandHandler(IAttendanceService attendance) : IRequestHandler<SaveAttendanceCommand, Result<AttendanceSheetDto>>
{
    public Task<Result<AttendanceSheetDto>> Handle(SaveAttendanceCommand r, CancellationToken cancellationToken) =>
        attendance.SaveSheetAsync(r.ClassId, r.Date, r.StartTime, r.Marks, cancellationToken);
}

public record GetAttendanceSummaryQuery(Guid ClassId, DateOnly From, DateOnly To) : IRequest<Result<IReadOnlyList<AttendanceSummaryRowDto>>>;

public class GetAttendanceSummaryQueryValidator : AbstractValidator<GetAttendanceSummaryQuery>
{
    public GetAttendanceSummaryQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber <= 366).WithName("to").WithMessage("At most one year at a time.");
    }
}

public class GetAttendanceSummaryQueryHandler(IAttendanceService attendance) : IRequestHandler<GetAttendanceSummaryQuery, Result<IReadOnlyList<AttendanceSummaryRowDto>>>
{
    public Task<Result<IReadOnlyList<AttendanceSummaryRowDto>>> Handle(GetAttendanceSummaryQuery r, CancellationToken cancellationToken) =>
        attendance.SummaryAsync(r.ClassId, r.From, r.To, cancellationToken);
}
