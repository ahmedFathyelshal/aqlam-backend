using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Assessments;

public enum QuestionType
{
    SingleChoice = 1,
    MultipleChoice = 2,
    TrueFalse = 3,
    ShortAnswer = 4
}

public class QuestionOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

/// <summary>
/// A reusable question in the organization's question bank (tenant-owned). Choice questions keep their
/// options inline; short-answer questions list the accepted answers.
/// </summary>
public class Question : BaseEntity
{
    public QuestionType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Explanation { get; set; }

    /// <summary>Default points when the question is added to a quiz.</summary>
    public decimal Points { get; set; } = 1;

    /// <summary>1 = easy, 2 = medium, 3 = hard.</summary>
    public int Difficulty { get; set; } = 2;

    public int? CurriculumId { get; set; }
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }

    public List<QuestionOption> Options { get; set; } = [];
    public List<string> AcceptedAnswers { get; set; } = [];

    public Guid CreatedByUserId { get; set; }
}
