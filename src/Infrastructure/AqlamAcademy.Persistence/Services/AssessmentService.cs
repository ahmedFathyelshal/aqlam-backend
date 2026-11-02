using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Assessments;
using AqlamAcademy.Domain.Assessments;
using AqlamAcademy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Question bank, quiz setup and quiz attempts. Attempts freeze the questions they were started with,
/// run on a server-side clock, and complete the lesson (via <see cref="LearnerAccess"/>) when passed.
/// </summary>
public class AssessmentService : IAssessmentService
{
    private readonly TenantDbContext _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly LearnerAccess _access;

    public AssessmentService(TenantDbContext tenant, ICurrentUserService currentUser, LearnerAccess access)
    {
        _tenant = tenant;
        _currentUser = currentUser;
        _access = access;
    }

    // ---- question bank ---------------------------------------------------------------------------------

    public async Task<IReadOnlyList<QuestionDto>> ListQuestionsAsync(int? subjectId, int? gradeId, string? search, CancellationToken cancellationToken)
    {
        var query = _tenant.Questions.AsNoTracking();
        if (subjectId.HasValue)
            query = query.Where(q => q.SubjectId == subjectId);
        if (gradeId.HasValue)
            query = query.Where(q => q.GradeId == gradeId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(q => EF.Functions.ILike(q.Text, $"%{search.Trim()}%"));

        var questions = await query.OrderByDescending(q => q.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);
        return questions.Select(ToDto).ToList();
    }

    public async Task<Result<QuestionDto>> SaveQuestionAsync(Guid? id, QuestionType type, string text, string? explanation, decimal points, int difficulty,
        int? curriculumId, int? gradeId, int? subjectId, IReadOnlyList<QuestionOptionInput> options, IReadOnlyList<string> acceptedAnswers,
        CancellationToken cancellationToken)
    {
        Question question;
        if (id is { } existingId)
        {
            var found = await _tenant.Questions.FirstOrDefaultAsync(q => q.Id == existingId, cancellationToken);
            if (found is null)
                return Result<QuestionDto>.NotFound("Question not found.");
            question = found;
        }
        else
        {
            question = new Question { CreatedByUserId = _currentUser.UserId };
            _tenant.Questions.Add(question);
        }

        question.Type = type;
        question.Text = text;
        question.Explanation = explanation;
        question.Points = points;
        question.Difficulty = difficulty;
        question.CurriculumId = curriculumId;
        question.GradeId = gradeId;
        question.SubjectId = subjectId;
        // Option ids are kept when sent, so earlier attempts (which store snapshots anyway) stay readable.
        question.Options = options.Select(o => new QuestionOption { Id = o.Id ?? Guid.NewGuid(), Text = o.Text.Trim(), IsCorrect = o.IsCorrect }).ToList();
        question.AcceptedAnswers = acceptedAnswers.Select(a => a.Trim()).ToList();
        question.LastModifiedAtUtc = DateTime.UtcNow;

        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<QuestionDto>.Success(ToDto(question));
    }

    public async Task<Result<bool>> DeleteQuestionAsync(Guid id, CancellationToken cancellationToken)
    {
        var question = await _tenant.Questions.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (question is null)
            return Result<bool>.NotFound("Question not found.");

        // Soft delete: quizzes stop using it for new attempts; past attempts keep their snapshot.
        question.IsDeleted = true;
        question.DeletedAtUtc = DateTime.UtcNow;
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    // ---- quiz setup ------------------------------------------------------------------------------------

    public async Task<Result<QuizDto>> GetQuizAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken)
    {
        if (await FindQuizLessonAsync(courseId, lessonId, cancellationToken) is { } failure)
            return Result<QuizDto>.Failure(failure.ErrorType, failure.Errors);

        var quiz = await LoadQuizAsync(lessonId, tracking: false, cancellationToken);
        return Result<QuizDto>.Success(quiz is null
            ? new QuizDto(lessonId, null, null, 60, false, false, true, [])
            : ToDto(quiz));
    }

    public async Task<Result<QuizDto>> SaveQuizAsync(Guid courseId, Guid lessonId, QuizSettingsInput settings, CancellationToken cancellationToken)
    {
        if (await FindQuizLessonAsync(courseId, lessonId, cancellationToken) is { } failure)
            return Result<QuizDto>.Failure(failure.ErrorType, failure.Errors);

        var questionIds = settings.Questions.Select(q => q.QuestionId).ToList();
        var questions = await _tenant.Questions.Where(q => questionIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id, cancellationToken);
        var unknown = questionIds.Where(id => !questions.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            return Result<QuizDto>.Failure(unknown.Select(id => $"Question {id} is not in this organization's question bank."));

        var quiz = await LoadQuizAsync(lessonId, tracking: true, cancellationToken);
        if (quiz is null)
        {
            quiz = new Quiz { CourseId = courseId, LessonId = lessonId };
            _tenant.Quizzes.Add(quiz);
        }

        quiz.TimeLimitMinutes = settings.TimeLimitMinutes;
        quiz.MaxAttempts = settings.MaxAttempts;
        quiz.PassPercent = settings.PassPercent;
        quiz.ShuffleQuestions = settings.ShuffleQuestions;
        quiz.ShuffleOptions = settings.ShuffleOptions;
        quiz.ShowAnswersAfterSubmit = settings.ShowAnswersAfterSubmit;
        quiz.LastModifiedAtUtc = DateTime.UtcNow;

        // The question list is plain configuration (attempts keep their own snapshots), so it is replaced.
        _tenant.RemoveRange(quiz.Questions);
        quiz.Questions = settings.Questions.Select((q, index) => new QuizQuestion
        {
            QuizId = quiz.Id,
            QuestionId = q.QuestionId,
            Order = index,
            Points = q.Points ?? questions[q.QuestionId].Points
        }).ToList();
        _tenant.AddRange(quiz.Questions);

        await _tenant.SaveChangesAsync(cancellationToken);
        _tenant.ChangeTracker.Clear();

        return Result<QuizDto>.Success(ToDto((await LoadQuizAsync(lessonId, tracking: false, cancellationToken))!));
    }

    // ---- attempts --------------------------------------------------------------------------------------

    public async Task<Result<QuizSummaryDto>> GetQuizSummaryAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var (_, lesson, failure) = await _access.ResolveUnlockedLessonForStudentAsync(lessonId, cancellationToken);
        if (failure is not null)
            return Result<QuizSummaryDto>.Failure(failure.ErrorType, failure.Errors);
        if (lesson!.Type != LessonType.Quiz)
            return Result<QuizSummaryDto>.NotFound("This lesson is not a quiz.");

        var quiz = await LoadQuizAsync(lessonId, tracking: false, cancellationToken);
        await AutoSubmitOverdueAsync(lessonId, cancellationToken);

        var attempts = await MyAttempts(lessonId).AsNoTracking().ToListAsync(cancellationToken);
        var submitted = attempts.Where(a => a.IsSubmitted).ToList();

        return Result<QuizSummaryDto>.Success(new QuizSummaryDto(
            lessonId,
            quiz?.Questions.Count ?? 0,
            quiz?.TimeLimitMinutes,
            quiz?.MaxAttempts,
            submitted.Count,
            quiz?.PassPercent ?? 60,
            submitted.Count == 0 ? null : submitted.Max(a => a.Percent),
            submitted.Any(a => a.Passed),
            attempts.FirstOrDefault(a => !a.IsSubmitted)?.Id));
    }

    public async Task<Result<AttemptDto>> StartAttemptAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var (_, lesson, failure) = await _access.ResolveUnlockedLessonForStudentAsync(lessonId, cancellationToken);
        if (failure is not null)
            return Result<AttemptDto>.Failure(failure.ErrorType, failure.Errors);
        if (lesson!.Type != LessonType.Quiz)
            return Result<AttemptDto>.NotFound("This lesson is not a quiz.");

        var quiz = await LoadQuizAsync(lessonId, tracking: false, cancellationToken);
        if (quiz is null || quiz.Questions.Count == 0)
            return Result<AttemptDto>.Conflict("This quiz is not ready yet.");

        await AutoSubmitOverdueAsync(lessonId, cancellationToken);

        // Resume an attempt that is still running instead of starting a second one.
        var running = await MyAttempts(lessonId).FirstOrDefaultAsync(a => a.SubmittedAtUtc == null, cancellationToken);
        if (running is not null)
            return Result<AttemptDto>.Success(ToDto(running));

        var used = await MyAttempts(lessonId).CountAsync(a => a.SubmittedAtUtc != null, cancellationToken);
        if (quiz.MaxAttempts is { } max && used >= max)
            return Result<AttemptDto>.Conflict($"You have used all {max} attempts for this quiz.");

        var now = DateTime.UtcNow;
        var orderedQuestions = quiz.Questions.OrderBy(q => q.Order).ToList();
        if (quiz.ShuffleQuestions)
            Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(orderedQuestions));

        var attempt = new QuizAttempt
        {
            QuizId = quiz.Id,
            CourseId = quiz.CourseId,
            LessonId = lessonId,
            UserId = _currentUser.UserId,
            StartedAtUtc = now,
            DeadlineUtc = quiz.TimeLimitMinutes is { } minutes ? now.AddMinutes(minutes) : null,
            Items = orderedQuestions.Select(q => Snapshot(q, quiz.ShuffleOptions)).ToList()
        };

        _tenant.QuizAttempts.Add(attempt);
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<AttemptDto>.Success(ToDto(attempt));
    }

    public async Task<Result<AttemptDto>> SaveAnswersAsync(Guid attemptId, IReadOnlyList<AnswerInput> answers, CancellationToken cancellationToken)
    {
        var (attempt, failure) = await FindMyRunningAttemptAsync(attemptId, cancellationToken);
        if (failure is not null)
            return Result<AttemptDto>.Failure(failure.ErrorType, failure.Errors);

        if (attempt!.IsOverdue(DateTime.UtcNow))
        {
            await SubmitAsync(attempt, autoSubmitted: true, cancellationToken);
            return Result<AttemptDto>.Conflict("Time is up. Your saved answers were submitted.");
        }

        if (ApplyAnswers(attempt, answers) is { } invalid)
            return Result<AttemptDto>.Failure(new[] { invalid });

        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<AttemptDto>.Success(ToDto(attempt));
    }

    public async Task<Result<AttemptResultDto>> SubmitAttemptAsync(Guid attemptId, IReadOnlyList<AnswerInput>? answers, CancellationToken cancellationToken)
    {
        var (attempt, failure) = await FindMyRunningAttemptAsync(attemptId, cancellationToken);
        if (failure is not null)
            return Result<AttemptResultDto>.Failure(failure.ErrorType, failure.Errors);

        // Answers sent after the deadline (plus grace) are ignored: the autosaved answers are graded.
        var overdue = attempt!.IsOverdue(DateTime.UtcNow);
        if (!overdue && answers is not null && ApplyAnswers(attempt, answers) is { } invalid)
            return Result<AttemptResultDto>.Failure(new[] { invalid });

        return Result<AttemptResultDto>.Success(await SubmitAsync(attempt, autoSubmitted: overdue, cancellationToken));
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private IQueryable<QuizAttempt> MyAttempts(Guid lessonId) =>
        _tenant.QuizAttempts.Where(a => a.LessonId == lessonId && a.UserId == _currentUser.UserId);

    private async Task<(QuizAttempt? Attempt, Result? Failure)> FindMyRunningAttemptAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await _tenant.QuizAttempts.FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == _currentUser.UserId, cancellationToken);
        if (attempt is null)
            return (null, Result<bool>.NotFound("Attempt not found."));
        if (attempt.IsSubmitted)
            return (null, Result<bool>.Conflict("This attempt was already submitted."));

        // The student must still have access (enrollment active, lesson unlocked).
        var (_, _, failure) = await _access.ResolveUnlockedLessonForStudentAsync(attempt.LessonId, cancellationToken);
        return failure is not null ? (null, failure) : (attempt, null);
    }

    private async Task AutoSubmitOverdueAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var running = await MyAttempts(lessonId).Where(a => a.SubmittedAtUtc == null && a.DeadlineUtc != null).ToListAsync(cancellationToken);
        foreach (var attempt in running.Where(a => a.IsOverdue(now)))
            await SubmitAsync(attempt, autoSubmitted: true, cancellationToken);
    }

    private async Task<AttemptResultDto> SubmitAsync(QuizAttempt attempt, bool autoSubmitted, CancellationToken cancellationToken)
    {
        var quiz = await _tenant.Quizzes.AsNoTracking().FirstAsync(q => q.Id == attempt.QuizId, cancellationToken);

        QuizGrading.GradeAttempt(attempt, quiz.PassPercent);
        attempt.SubmittedAtUtc = DateTime.UtcNow;
        attempt.AutoSubmitted = autoSubmitted;
        await _tenant.SaveChangesAsync(cancellationToken);

        var lessonCompleted = false;
        if (attempt.Passed)
        {
            // Passing completes the lesson (once), exactly like finishing a video.
            var (access, _, failure) = await _access.ResolveUnlockedLessonForStudentAsync(attempt.LessonId, cancellationToken);
            if (failure is null)
            {
                var (progress, _, _) = await _access.RecordAsync(access.Course!, access.Enrollment!, attempt.LessonId,
                    update: _ => { }, complete: _ => true, cancellationToken);
                lessonCompleted = progress.IsCompleted;
            }
        }

        var items = quiz.ShowAnswersAfterSubmit
            ? attempt.Items.Select(i => new AttemptResultItemDto(
                i.QuestionId, i.IsCorrect, i.AwardedPoints, i.Points, i.SelectedOptionIds, i.TextAnswer,
                i.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList(), i.AcceptedAnswers, i.Explanation)).ToList()
            : null;

        return new AttemptResultDto(attempt.Id, attempt.Score, attempt.MaxScore, attempt.Percent, attempt.Passed, attempt.AutoSubmitted, lessonCompleted, items);
    }

    /// <summary>Applies answers to the attempt; returns an error message when an answer does not fit its question.</summary>
    private static string? ApplyAnswers(QuizAttempt attempt, IReadOnlyList<AnswerInput> answers)
    {
        foreach (var answer in answers)
        {
            var item = attempt.Items.FirstOrDefault(i => i.QuestionId == answer.QuestionId);
            if (item is null)
                return $"Question {answer.QuestionId} is not part of this attempt.";

            if (item.Type == QuestionType.ShortAnswer)
            {
                item.TextAnswer = answer.TextAnswer?.Trim();
                continue;
            }

            var selected = (answer.SelectedOptionIds ?? []).Distinct().ToList();
            if (selected.Any(id => item.Options.All(o => o.Id != id)))
                return $"An option of question {answer.QuestionId} is not valid.";
            if (item.Type is QuestionType.SingleChoice or QuestionType.TrueFalse && selected.Count > 1)
                return $"Question {answer.QuestionId} accepts a single answer.";

            item.SelectedOptionIds = selected;
        }

        return null;
    }

    private static AttemptItem Snapshot(QuizQuestion quizQuestion, bool shuffleOptions)
    {
        var question = quizQuestion.Question;
        var options = question.Options.Select(o => new QuestionOption { Id = o.Id, Text = o.Text, IsCorrect = o.IsCorrect }).ToList();
        if (shuffleOptions && question.Type != QuestionType.TrueFalse)
            Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(options));

        return new AttemptItem
        {
            QuestionId = question.Id,
            Type = question.Type,
            Text = question.Text,
            Explanation = question.Explanation,
            Points = quizQuestion.Points,
            Options = options,
            AcceptedAnswers = [.. question.AcceptedAnswers]
        };
    }

    private async Task<Result?> FindQuizLessonAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken)
    {
        var lesson = await _tenant.CourseLessons.AsNoTracking()
            .Where(l => l.Id == lessonId && l.Chapter.CourseId == courseId)
            .Select(l => new { l.Type })
            .FirstOrDefaultAsync(cancellationToken);

        if (lesson is null)
            return Result<bool>.NotFound("Lesson not found in this course.");
        return lesson.Type == LessonType.Quiz ? null : Result<bool>.Conflict("This lesson is not a quiz. Change its type to Quiz first.");
    }

    /// <summary>Loads the quiz with its (non-deleted) questions.</summary>
    private async Task<Quiz?> LoadQuizAsync(Guid lessonId, bool tracking, CancellationToken cancellationToken)
    {
        var query = _tenant.Quizzes.Include(q => q.Questions).ThenInclude(qq => qq.Question).AsQueryable();
        var quiz = await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(q => q.LessonId == lessonId, cancellationToken);

        // A required navigation to a soft-deleted question comes back null: those questions are skipped.
        if (quiz is not null && !tracking)
            quiz.Questions = quiz.Questions.Where(q => q.Question is not null).ToList();
        return quiz;
    }

    private static QuestionDto ToDto(Question q) => new(
        q.Id, q.Type.ToString(), q.Text, q.Explanation, q.Points, q.Difficulty, q.CurriculumId, q.GradeId, q.SubjectId,
        q.Options.Select(o => new QuestionOptionDto(o.Id, o.Text, o.IsCorrect)).ToList(),
        q.AcceptedAnswers);

    private static QuizDto ToDto(Quiz quiz) => new(
        quiz.LessonId, quiz.TimeLimitMinutes, quiz.MaxAttempts, quiz.PassPercent, quiz.ShuffleQuestions, quiz.ShuffleOptions,
        quiz.ShowAnswersAfterSubmit,
        quiz.Questions.OrderBy(q => q.Order).Select(q => new QuizQuestionDto(q.QuestionId, q.Order, q.Points, ToDto(q.Question))).ToList());

    private static AttemptDto ToDto(QuizAttempt attempt) => new(
        attempt.Id, attempt.LessonId, attempt.StartedAtUtc, attempt.DeadlineUtc,
        attempt.Items.Select(i => new AttemptQuestionDto(
            i.QuestionId, i.Type.ToString(), i.Text, i.Points,
            i.Options.Select(o => new AttemptOptionDto(o.Id, o.Text)).ToList(), // correctness never leaves the server
            i.SelectedOptionIds, i.TextAnswer)).ToList());
}
