namespace AqlamAcademy.Domain.Marketplace;

/// <summary>
/// A student's rating of a public course (story 3.7.1). Only a student enrolled in the course who has really started
/// it can review, once per course; the text is optional. Ratings are shown, and used to rank, only after
/// <see cref="MinRatingsToShow"/> reviews, so one or two opinions never make a ranking.
/// </summary>
public class CourseReview
{
    /// <summary>Progress a student must have made before reviewing, so a course cannot be rated unseen.</summary>
    public const int MinProgressPercent = 10;
    /// <summary>Reviews needed before an average (and a place in a top list) is shown.</summary>
    public const int MinRatingsToShow = 5;
    public const int MaxCommentLength = 1000;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public Guid OrganizationId { get; set; }
    /// <summary>The student. Never shown with a full name; see the review service.</summary>
    public Guid UserId { get; set; }

    public int Stars { get; set; }
    public string? Comment { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Hidden by a platform administrator (abuse, a personal detail). Hidden reviews do not count.</summary>
    public bool IsHidden { get; set; }
    public string? HiddenReason { get; set; }
    public DateTime? HiddenAtUtc { get; set; }
    public Guid? HiddenByUserId { get; set; }
}
