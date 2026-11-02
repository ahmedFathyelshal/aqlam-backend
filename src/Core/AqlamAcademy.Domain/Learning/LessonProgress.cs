using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Learning;

/// <summary>A student's progress in one lesson. Tenant-owned (stored next to the lessons).</summary>
public class LessonProgress : BaseEntity
{
    public const int VideoCompletionThreshold = 90;

    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public Guid LessonId { get; set; }

    /// <summary>Highest percentage of the video watched (never decreases).</summary>
    public int WatchedPercent { get; set; }
    public int LastPositionSeconds { get; set; }

    /// <summary>Set once, the first time the lesson is completed; later activity does not change it.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    public bool IsCompleted => CompletedAtUtc.HasValue;
}
