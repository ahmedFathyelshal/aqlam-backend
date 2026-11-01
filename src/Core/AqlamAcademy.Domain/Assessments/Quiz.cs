using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Assessments;

/// <summary>Settings and questions of a lesson of type Quiz (one quiz per lesson).</summary>
public class Quiz : BaseEntity
{
    public Guid CourseId { get; set; }
    public Guid LessonId { get; set; }

    /// <summary>Null = no time limit.</summary>
    public int? TimeLimitMinutes { get; set; }
    /// <summary>Null = unlimited attempts.</summary>
    public int? MaxAttempts { get; set; }
    public int PassPercent { get; set; } = 60;
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public bool ShowAnswersAfterSubmit { get; set; } = true;

    public List<QuizQuestion> Questions { get; set; } = [];
}

public class QuizQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuizId { get; set; }
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int Order { get; set; }
    public decimal Points { get; set; }
}

/// <summary>Frozen copy of a question as it was when the attempt started (grading never depends on later edits).</summary>
public class AttemptItem
{
    public Guid QuestionId { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Explanation { get; set; }
    public decimal Points { get; set; }
    public List<QuestionOption> Options { get; set; } = []; // in the order shown to the student
    public List<string> AcceptedAnswers { get; set; } = [];

    // Filled at submission
    public List<Guid> SelectedOptionIds { get; set; } = [];
    public string? TextAnswer { get; set; }
    public decimal AwardedPoints { get; set; }
    public bool IsCorrect { get; set; }
}

public class QuizAttempt : BaseEntity
{
    public static readonly TimeSpan SubmissionGrace = TimeSpan.FromSeconds(30);

    public Guid QuizId { get; set; }
    public Guid CourseId { get; set; }
    public Guid LessonId { get; set; }
    public Guid UserId { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Server-side deadline; null when the quiz has no time limit.</summary>
    public DateTime? DeadlineUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }

    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public decimal Percent { get; set; }
    public bool Passed { get; set; }
    /// <summary>True when the server submitted the attempt because time ran out.</summary>
    public bool AutoSubmitted { get; set; }

    public List<AttemptItem> Items { get; set; } = [];

    public bool IsSubmitted => SubmittedAtUtc.HasValue;

    /// <summary>Past the deadline including the grace period for network delays.</summary>
    public bool IsOverdue(DateTime now) => DeadlineUtc is { } deadline && now > deadline + SubmissionGrace;
}
