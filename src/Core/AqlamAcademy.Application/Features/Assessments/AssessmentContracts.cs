using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Assessments;

namespace AqlamAcademy.Application.Features.Assessments;

// ---- staff: question bank and quiz setup ---------------------------------------------------------------

public record QuestionOptionDto(Guid Id, string Text, bool IsCorrect);

public record QuestionDto(
    Guid Id,
    string Type,
    string Text,
    string? Explanation,
    decimal Points,
    int Difficulty,
    int? CurriculumId,
    int? GradeId,
    int? SubjectId,
    IReadOnlyList<QuestionOptionDto> Options,
    IReadOnlyList<string> AcceptedAnswers);

public record QuestionOptionInput(Guid? Id, string Text, bool IsCorrect);

public record QuizQuestionDto(Guid QuestionId, int Order, decimal Points, QuestionDto Question);

public record QuizDto(
    Guid LessonId,
    int? TimeLimitMinutes,
    int? MaxAttempts,
    int PassPercent,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    bool ShowAnswersAfterSubmit,
    IReadOnlyList<QuizQuestionDto> Questions);

public record QuizQuestionInput(Guid QuestionId, decimal? Points);

public record QuizSettingsInput(
    int? TimeLimitMinutes,
    int? MaxAttempts,
    int PassPercent,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    bool ShowAnswersAfterSubmit,
    IReadOnlyList<QuizQuestionInput> Questions);

// ---- students: taking a quiz ---------------------------------------------------------------------------

public record QuizSummaryDto(
    Guid LessonId,
    int QuestionCount,
    int? TimeLimitMinutes,
    int? MaxAttempts,
    int AttemptsUsed,
    int PassPercent,
    decimal? BestPercent,
    bool Passed,
    Guid? InProgressAttemptId);

public record AttemptOptionDto(Guid Id, string Text);

/// <summary>A question as shown during an attempt: never includes which options are correct.</summary>
public record AttemptQuestionDto(
    Guid QuestionId,
    string Type,
    string Text,
    decimal Points,
    IReadOnlyList<AttemptOptionDto> Options,
    IReadOnlyList<Guid> SelectedOptionIds,
    string? TextAnswer);

public record AttemptDto(
    Guid Id,
    Guid LessonId,
    DateTime StartedAtUtc,
    DateTime? DeadlineUtc,
    IReadOnlyList<AttemptQuestionDto> Questions);

public record AnswerInput(Guid QuestionId, IReadOnlyList<Guid>? SelectedOptionIds, string? TextAnswer);

public record AttemptResultItemDto(
    Guid QuestionId,
    bool IsCorrect,
    decimal AwardedPoints,
    decimal Points,
    IReadOnlyList<Guid> SelectedOptionIds,
    string? TextAnswer,
    IReadOnlyList<Guid> CorrectOptionIds,
    IReadOnlyList<string> AcceptedAnswers,
    string? Explanation);

/// <summary>Items are only included when the quiz shows answers after submission.</summary>
public record AttemptResultDto(
    Guid AttemptId,
    decimal Score,
    decimal MaxScore,
    decimal Percent,
    bool Passed,
    bool AutoSubmitted,
    bool LessonCompleted,
    IReadOnlyList<AttemptResultItemDto>? Items);

public interface IAssessmentService
{
    Task<IReadOnlyList<QuestionDto>> ListQuestionsAsync(int? subjectId, int? gradeId, string? search, CancellationToken cancellationToken);
    Task<Result<QuestionDto>> SaveQuestionAsync(Guid? id, QuestionType type, string text, string? explanation, decimal points, int difficulty,
        int? curriculumId, int? gradeId, int? subjectId, IReadOnlyList<QuestionOptionInput> options, IReadOnlyList<string> acceptedAnswers,
        CancellationToken cancellationToken);
    Task<Result<bool>> DeleteQuestionAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<QuizDto>> GetQuizAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken);
    Task<Result<QuizDto>> SaveQuizAsync(Guid courseId, Guid lessonId, QuizSettingsInput settings, CancellationToken cancellationToken);

    Task<Result<QuizSummaryDto>> GetQuizSummaryAsync(Guid lessonId, CancellationToken cancellationToken);
    Task<Result<AttemptDto>> StartAttemptAsync(Guid lessonId, CancellationToken cancellationToken);
    Task<Result<AttemptDto>> SaveAnswersAsync(Guid attemptId, IReadOnlyList<AnswerInput> answers, CancellationToken cancellationToken);
    Task<Result<AttemptResultDto>> SubmitAttemptAsync(Guid attemptId, IReadOnlyList<AnswerInput>? answers, CancellationToken cancellationToken);
}
