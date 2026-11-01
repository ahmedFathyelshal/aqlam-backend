using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Domain.Learning;

public enum LockReason
{
    None = 0,
    CourseNotStarted = 1,     // course is Scheduled ("coming soon")
    DripSchedule = 2,         // unlocks N days after enrollment
    PreviousLessonRequired = 3
}

public sealed record LessonAccessState(Guid LessonId, bool IsLocked, LockReason Reason, DateTime? UnlocksAtUtc);

/// <summary>
/// Decides which lessons a student can open. Pure function of the course structure, the enrollment start
/// and the lessons the student has completed, so it is easy to test and identical everywhere it is used.
/// Unpublished lessons are not visible to students at all and are excluded.
/// </summary>
public static class LessonAccess
{
    /// <summary>Lessons as a student sees them: published only, in chapter order then lesson order.</summary>
    public static IReadOnlyList<CourseLesson> VisibleLessons(Course course) =>
        course.Chapters
            .OrderBy(c => c.Order)
            .SelectMany(c => c.Lessons.Where(l => l.IsPublished).OrderBy(l => l.Order))
            .ToList();

    public static IReadOnlyDictionary<Guid, LessonAccessState> Evaluate(
        Course course,
        DateTime enrollmentStartUtc,
        IReadOnlySet<Guid> completedLessonIds,
        DateTime now)
    {
        var result = new Dictionary<Guid, LessonAccessState>();
        CourseLesson? previous = null;

        foreach (var lesson in VisibleLessons(course))
        {
            var state = Decide(course, lesson, previous, enrollmentStartUtc, completedLessonIds, now);
            result[lesson.Id] = state;
            previous = lesson;
        }

        return result;
    }

    private static LessonAccessState Decide(
        Course course,
        CourseLesson lesson,
        CourseLesson? previous,
        DateTime enrollmentStartUtc,
        IReadOnlySet<Guid> completed,
        DateTime now)
    {
        if (course.Status == CourseStatus.Scheduled)
            return new(lesson.Id, true, LockReason.CourseNotStarted, null);

        if (lesson.UnlockDaysAfterEnrollment > 0)
        {
            var unlocksAt = enrollmentStartUtc.AddDays(lesson.UnlockDaysAfterEnrollment);
            if (now < unlocksAt)
                return new(lesson.Id, true, LockReason.DripSchedule, unlocksAt);
        }

        if (lesson.RequirePreviousLessonCompletion && previous is not null && !completed.Contains(previous.Id))
            return new(lesson.Id, true, LockReason.PreviousLessonRequired, null);

        return new(lesson.Id, false, LockReason.None, null);
    }
}
