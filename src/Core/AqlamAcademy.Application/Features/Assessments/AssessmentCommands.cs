using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Assessments;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Assessments;

// ---- question bank -------------------------------------------------------------------------------------

public record ListQuestionsQuery(int? SubjectId, int? GradeId, string? Search) : IRequest<Result<IReadOnlyList<QuestionDto>>>;

public class ListQuestionsQueryHandler(IAssessmentService assessments) : IRequestHandler<ListQuestionsQuery, Result<IReadOnlyList<QuestionDto>>>
{
    public async Task<Result<IReadOnlyList<QuestionDto>>> Handle(ListQuestionsQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<QuestionDto>>.Success(await assessments.ListQuestionsAsync(request.SubjectId, request.GradeId, request.Search, cancellationToken));
}

/// <summary>Creates (Id = null) or updates a question in the organization's bank.</summary>
public record SaveQuestionCommand(
    Guid? Id,
    QuestionType Type,
    string Text,
    string? Explanation,
    decimal Points,
    int Difficulty,
    int? CurriculumId,
    int? GradeId,
    int? SubjectId,
    IReadOnlyList<QuestionOptionInput>? Options,
    IReadOnlyList<string>? AcceptedAnswers) : IRequest<Result<QuestionDto>>;

public class SaveQuestionCommandValidator : AbstractValidator<SaveQuestionCommand>
{
    public SaveQuestionCommandValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Text).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Explanation).MaximumLength(4000);
        RuleFor(x => x.Points).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.Difficulty).InclusiveBetween(1, 3);
        RuleForEach(x => x.Options).ChildRules(option => option.RuleFor(o => o.Text).NotEmpty().MaximumLength(1000));
        RuleForEach(x => x.AcceptedAnswers).NotEmpty().MaximumLength(500);

        When(x => x.Type is QuestionType.SingleChoice or QuestionType.TrueFalse, () =>
        {
            RuleFor(x => x.Options).Must(o => o is { Count: >= 2 and <= 10 }).WithMessage("Add between 2 and 10 options.");
            RuleFor(x => x.Options).Must(o => o?.Count(x => x.IsCorrect) == 1).WithMessage("Mark exactly one option as correct.");
        });
        When(x => x.Type == QuestionType.TrueFalse, () =>
            RuleFor(x => x.Options).Must(o => o?.Count == 2).WithMessage("A true/false question has exactly two options."));
        When(x => x.Type == QuestionType.MultipleChoice, () =>
        {
            RuleFor(x => x.Options).Must(o => o is { Count: >= 2 and <= 10 }).WithMessage("Add between 2 and 10 options.");
            RuleFor(x => x.Options).Must(o => o?.Any(x => x.IsCorrect) == true).WithMessage("Mark at least one option as correct.");
        });
        When(x => x.Type == QuestionType.ShortAnswer, () =>
        {
            RuleFor(x => x.AcceptedAnswers).Must(a => a is { Count: >= 1 and <= 20 }).WithMessage("Add between 1 and 20 accepted answers.");
            RuleFor(x => x.Options).Must(o => o is null || o.Count == 0).WithMessage("Short-answer questions have no options.");
        });
    }
}

public class SaveQuestionCommandHandler(IAssessmentService assessments) : IRequestHandler<SaveQuestionCommand, Result<QuestionDto>>
{
    public Task<Result<QuestionDto>> Handle(SaveQuestionCommand r, CancellationToken cancellationToken) =>
        assessments.SaveQuestionAsync(r.Id, r.Type, r.Text.Trim(), r.Explanation, r.Points, r.Difficulty, r.CurriculumId, r.GradeId, r.SubjectId,
            r.Options ?? [], r.AcceptedAnswers ?? [], cancellationToken);
}

public record DeleteQuestionCommand(Guid Id) : IRequest<Result<bool>>;

public class DeleteQuestionCommandHandler(IAssessmentService assessments) : IRequestHandler<DeleteQuestionCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(DeleteQuestionCommand request, CancellationToken cancellationToken) =>
        assessments.DeleteQuestionAsync(request.Id, cancellationToken);
}

// ---- quiz setup ----------------------------------------------------------------------------------------

public record GetQuizQuery(Guid CourseId, Guid LessonId) : IRequest<Result<QuizDto>>;

public class GetQuizQueryHandler(IAssessmentService assessments) : IRequestHandler<GetQuizQuery, Result<QuizDto>>
{
    public Task<Result<QuizDto>> Handle(GetQuizQuery request, CancellationToken cancellationToken) =>
        assessments.GetQuizAsync(request.CourseId, request.LessonId, cancellationToken);
}

public record SaveQuizCommand(Guid CourseId, Guid LessonId, QuizSettingsInput Settings) : IRequest<Result<QuizDto>>;

public class SaveQuizCommandValidator : AbstractValidator<SaveQuizCommand>
{
    public SaveQuizCommandValidator()
    {
        RuleFor(x => x.Settings).NotNull();
        RuleFor(x => x.Settings.TimeLimitMinutes).InclusiveBetween(1, 600).When(x => x.Settings?.TimeLimitMinutes.HasValue == true);
        RuleFor(x => x.Settings.MaxAttempts).InclusiveBetween(1, 100).When(x => x.Settings?.MaxAttempts.HasValue == true);
        RuleFor(x => x.Settings.PassPercent).InclusiveBetween(0, 100).When(x => x.Settings is not null);
        RuleFor(x => x.Settings.Questions).NotNull().Must(q => q.Count <= 200).When(x => x.Settings is not null)
            .WithMessage("A quiz can have at most 200 questions.");
        RuleFor(x => x.Settings.Questions)
            .Must(q => q.Select(x => x.QuestionId).Distinct().Count() == q.Count)
            .When(x => x.Settings?.Questions is not null)
            .WithMessage("A question appears more than once.");
        RuleForEach(x => x.Settings.Questions).ChildRules(q => q.RuleFor(x => x.Points).GreaterThan(0).LessThanOrEqualTo(100).When(x => x.Points.HasValue))
            .When(x => x.Settings?.Questions is not null);
    }
}

public class SaveQuizCommandHandler(IAssessmentService assessments) : IRequestHandler<SaveQuizCommand, Result<QuizDto>>
{
    public Task<Result<QuizDto>> Handle(SaveQuizCommand request, CancellationToken cancellationToken) =>
        assessments.SaveQuizAsync(request.CourseId, request.LessonId, request.Settings, cancellationToken);
}

// ---- taking a quiz -------------------------------------------------------------------------------------

public record GetQuizSummaryQuery(Guid LessonId) : IRequest<Result<QuizSummaryDto>>;

public class GetQuizSummaryQueryHandler(IAssessmentService assessments) : IRequestHandler<GetQuizSummaryQuery, Result<QuizSummaryDto>>
{
    public Task<Result<QuizSummaryDto>> Handle(GetQuizSummaryQuery request, CancellationToken cancellationToken) =>
        assessments.GetQuizSummaryAsync(request.LessonId, cancellationToken);
}

public record StartAttemptCommand(Guid LessonId) : IRequest<Result<AttemptDto>>;

public class StartAttemptCommandHandler(IAssessmentService assessments) : IRequestHandler<StartAttemptCommand, Result<AttemptDto>>
{
    public Task<Result<AttemptDto>> Handle(StartAttemptCommand request, CancellationToken cancellationToken) =>
        assessments.StartAttemptAsync(request.LessonId, cancellationToken);
}

public record SaveAnswersCommand(Guid AttemptId, IReadOnlyList<AnswerInput> Answers) : IRequest<Result<AttemptDto>>;

public class SaveAnswersCommandValidator : AbstractValidator<SaveAnswersCommand>
{
    public SaveAnswersCommandValidator()
    {
        RuleFor(x => x.Answers).NotNull().Must(a => a.Count <= 200);
        RuleForEach(x => x.Answers).ChildRules(a => a.RuleFor(x => x.TextAnswer).MaximumLength(500));
    }
}

public class SaveAnswersCommandHandler(IAssessmentService assessments) : IRequestHandler<SaveAnswersCommand, Result<AttemptDto>>
{
    public Task<Result<AttemptDto>> Handle(SaveAnswersCommand request, CancellationToken cancellationToken) =>
        assessments.SaveAnswersAsync(request.AttemptId, request.Answers, cancellationToken);
}

public record SubmitAttemptCommand(Guid AttemptId, IReadOnlyList<AnswerInput>? Answers) : IRequest<Result<AttemptResultDto>>;

public class SubmitAttemptCommandValidator : AbstractValidator<SubmitAttemptCommand>
{
    public SubmitAttemptCommandValidator()
    {
        RuleFor(x => x.Answers).Must(a => a is null || a.Count <= 200);
        RuleForEach(x => x.Answers).ChildRules(a => a.RuleFor(x => x.TextAnswer).MaximumLength(500));
    }
}

public class SubmitAttemptCommandHandler(IAssessmentService assessments) : IRequestHandler<SubmitAttemptCommand, Result<AttemptResultDto>>
{
    public Task<Result<AttemptResultDto>> Handle(SubmitAttemptCommand request, CancellationToken cancellationToken) =>
        assessments.SubmitAttemptAsync(request.AttemptId, request.Answers, cancellationToken);
}
