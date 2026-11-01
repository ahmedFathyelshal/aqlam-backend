using AqlamAcademy.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Assessments;

public record AssignmentSettingsDto(Guid LessonId, decimal MaxPoints, DateTime? DueAtUtc, bool AcceptLateSubmissions);

public record SubmissionDto(
    Guid Id,
    Guid LessonId,
    Guid UserId,
    string? StudentUserName,
    string? StudentName,
    string? LessonTitle,
    string? Text,
    IReadOnlyList<string> Links,
    DateTime SubmittedAtUtc,
    bool IsLate,
    string Status,
    decimal? Score,
    decimal MaxPoints,
    string? Feedback,
    DateTime? GradedAtUtc);

/// <summary>What the student sees: instructions are the lesson content; their submission if any.</summary>
public record StudentAssignmentDto(
    Guid LessonId,
    decimal MaxPoints,
    DateTime? DueAtUtc,
    bool AcceptLateSubmissions,
    bool CanSubmit,
    SubmissionDto? Submission);

public interface IAssignmentService
{
    Task<Result<AssignmentSettingsDto>> GetSettingsAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken);
    Task<Result<AssignmentSettingsDto>> SaveSettingsAsync(Guid courseId, Guid lessonId, decimal maxPoints, DateTime? dueAtUtc, bool acceptLate, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<SubmissionDto>>> ListSubmissionsAsync(Guid courseId, bool pendingOnly, CancellationToken cancellationToken);
    Task<Result<SubmissionDto>> GradeAsync(Guid submissionId, decimal? score, string? feedback, bool returnForRevision, CancellationToken cancellationToken);

    Task<Result<StudentAssignmentDto>> GetForStudentAsync(Guid lessonId, CancellationToken cancellationToken);
    Task<Result<SubmissionDto>> SubmitAsync(Guid lessonId, string? text, IReadOnlyList<string> links, CancellationToken cancellationToken);
}

public record GetAssignmentSettingsQuery(Guid CourseId, Guid LessonId) : IRequest<Result<AssignmentSettingsDto>>;

public class GetAssignmentSettingsQueryHandler(IAssignmentService assignments) : IRequestHandler<GetAssignmentSettingsQuery, Result<AssignmentSettingsDto>>
{
    public Task<Result<AssignmentSettingsDto>> Handle(GetAssignmentSettingsQuery request, CancellationToken cancellationToken) =>
        assignments.GetSettingsAsync(request.CourseId, request.LessonId, cancellationToken);
}

public record SaveAssignmentSettingsCommand(Guid CourseId, Guid LessonId, decimal MaxPoints, DateTime? DueAtUtc, bool AcceptLateSubmissions)
    : IRequest<Result<AssignmentSettingsDto>>;

public class SaveAssignmentSettingsCommandValidator : AbstractValidator<SaveAssignmentSettingsCommand>
{
    public SaveAssignmentSettingsCommandValidator()
    {
        RuleFor(x => x.MaxPoints).GreaterThan(0).LessThanOrEqualTo(1000);
    }
}

public class SaveAssignmentSettingsCommandHandler(IAssignmentService assignments) : IRequestHandler<SaveAssignmentSettingsCommand, Result<AssignmentSettingsDto>>
{
    public Task<Result<AssignmentSettingsDto>> Handle(SaveAssignmentSettingsCommand r, CancellationToken cancellationToken) =>
        assignments.SaveSettingsAsync(r.CourseId, r.LessonId, r.MaxPoints, r.DueAtUtc, r.AcceptLateSubmissions, cancellationToken);
}

/// <summary>Grading queue of a course (pendingOnly = waiting for a grade).</summary>
public record ListSubmissionsQuery(Guid CourseId, bool PendingOnly) : IRequest<Result<IReadOnlyList<SubmissionDto>>>;

public class ListSubmissionsQueryHandler(IAssignmentService assignments) : IRequestHandler<ListSubmissionsQuery, Result<IReadOnlyList<SubmissionDto>>>
{
    public Task<Result<IReadOnlyList<SubmissionDto>>> Handle(ListSubmissionsQuery request, CancellationToken cancellationToken) =>
        assignments.ListSubmissionsAsync(request.CourseId, request.PendingOnly, cancellationToken);
}

public record GradeSubmissionCommand(Guid SubmissionId, decimal? Score, string? Feedback, bool ReturnForRevision) : IRequest<Result<SubmissionDto>>;

public class GradeSubmissionCommandValidator : AbstractValidator<GradeSubmissionCommand>
{
    public GradeSubmissionCommandValidator()
    {
        RuleFor(x => x.Score).NotNull().GreaterThanOrEqualTo(0).When(x => !x.ReturnForRevision)
            .WithMessage("A score is required unless the work is returned for revision.");
        RuleFor(x => x.Feedback).MaximumLength(4000);
        RuleFor(x => x.Feedback).NotEmpty().When(x => x.ReturnForRevision)
            .WithMessage("Tell the student what to revise.");
    }
}

public class GradeSubmissionCommandHandler(IAssignmentService assignments) : IRequestHandler<GradeSubmissionCommand, Result<SubmissionDto>>
{
    public Task<Result<SubmissionDto>> Handle(GradeSubmissionCommand r, CancellationToken cancellationToken) =>
        assignments.GradeAsync(r.SubmissionId, r.Score, r.Feedback, r.ReturnForRevision, cancellationToken);
}

public record GetStudentAssignmentQuery(Guid LessonId) : IRequest<Result<StudentAssignmentDto>>;

public class GetStudentAssignmentQueryHandler(IAssignmentService assignments) : IRequestHandler<GetStudentAssignmentQuery, Result<StudentAssignmentDto>>
{
    public Task<Result<StudentAssignmentDto>> Handle(GetStudentAssignmentQuery request, CancellationToken cancellationToken) =>
        assignments.GetForStudentAsync(request.LessonId, cancellationToken);
}

public record SubmitAssignmentCommand(Guid LessonId, string? Text, IReadOnlyList<string>? Links) : IRequest<Result<SubmissionDto>>;

public class SubmitAssignmentCommandValidator : AbstractValidator<SubmitAssignmentCommand>
{
    public SubmitAssignmentCommandValidator()
    {
        RuleFor(x => x.Text).MaximumLength(20000);
        RuleFor(x => x.Links).Must(l => l is null || l.Count <= 10).WithMessage("At most 10 links.");
        RuleForEach(x => x.Links)
            .Must(link => Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("Links must be https URLs.");
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Text) || x.Links is { Count: > 0 })
            .WithName("text").WithMessage("Write an answer or add at least one link.");
    }
}

public class SubmitAssignmentCommandHandler(IAssignmentService assignments) : IRequestHandler<SubmitAssignmentCommand, Result<SubmissionDto>>
{
    public Task<Result<SubmissionDto>> Handle(SubmitAssignmentCommand r, CancellationToken cancellationToken) =>
        assignments.SubmitAsync(r.LessonId, r.Text, r.Links ?? [], cancellationToken);
}
