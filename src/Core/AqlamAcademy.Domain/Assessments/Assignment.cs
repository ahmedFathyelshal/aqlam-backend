using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Assessments;

/// <summary>Settings of a lesson of type Assignment; the lesson content holds the instructions.</summary>
public class Assignment : BaseEntity
{
    public Guid CourseId { get; set; }
    public Guid LessonId { get; set; }

    public decimal MaxPoints { get; set; } = 10;
    /// <summary>Null = no due date.</summary>
    public DateTime? DueAtUtc { get; set; }
    public bool AcceptLateSubmissions { get; set; } = true;
}

public enum SubmissionStatus
{
    Submitted = 1,
    Graded = 2,
    ReturnedForRevision = 3
}

/// <summary>A student's work for an assignment (one per student; resubmitting updates it until graded).</summary>
public class AssignmentSubmission : BaseEntity
{
    public Guid AssignmentId { get; set; }
    public Guid CourseId { get; set; }
    public Guid LessonId { get; set; }
    public Guid UserId { get; set; }

    public string? Text { get; set; }
    public List<string> Links { get; set; } = [];

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsLate { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Submitted;

    public decimal? Score { get; set; }
    public string? Feedback { get; set; }
    public Guid? GradedByUserId { get; set; }
    public DateTime? GradedAtUtc { get; set; }

    /// <summary>Students may change their work until it is graded, or when it was returned for revision.</summary>
    public bool CanBeChangedByStudent => Status != SubmissionStatus.Graded;
}
