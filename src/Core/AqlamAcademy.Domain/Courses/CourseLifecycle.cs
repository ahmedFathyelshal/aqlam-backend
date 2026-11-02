using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Domain.Courses;

/// <summary>
/// Course status rules: Draft (under construction) → Scheduled ("coming soon") → Published (live) → Archived
/// (hidden from the catalogue, enrolled students keep access). Suspended is a platform-admin takedown and is
/// not reachable through these transitions.
/// </summary>
public static class CourseLifecycle
{
    private static readonly Dictionary<CourseStatus, CourseStatus[]> AllowedTransitions = new()
    {
        [CourseStatus.Draft] = [CourseStatus.Scheduled, CourseStatus.Published],
        [CourseStatus.Scheduled] = [CourseStatus.Published, CourseStatus.Draft],
        [CourseStatus.Published] = [CourseStatus.Archived, CourseStatus.Draft],
        [CourseStatus.Archived] = [CourseStatus.Published],
        [CourseStatus.Suspended] = []
    };

    public static bool CanTransition(CourseStatus from, CourseStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Statuses that expose the course to students and therefore require a complete course.</summary>
    public static bool RequiresCompleteCourse(CourseStatus status) =>
        status is CourseStatus.Scheduled or CourseStatus.Published;

    /// <summary>
    /// Everything that must be fixed before the course can be scheduled or published. Empty = ready.
    /// Drafts may be incomplete; this is the "strict validation" applied at publish time.
    /// </summary>
    public static IReadOnlyList<string> GetPublishIssues(Course course)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(course.Title))
            issues.Add("Add a course title.");
        if (course.CurriculumId is null || course.StageId is null || course.GradeId is null || course.SubjectId is null)
            issues.Add("Choose the curriculum, stage, grade and subject.");

        if (course.PricingType is null)
            issues.Add("Choose whether the course is free or paid.");
        else if (course.PricingType == PricingType.Paid && !course.CodeActivationOnly && (course.Price is null or <= 0))
            issues.Add("Set a price greater than zero for a paid course.");

        if (course.AccessExpiryPolicy is null)
            issues.Add("Choose how long students keep access.");
        else if (course.AccessExpiryPolicy == AccessExpiryPolicy.Limited && course.AccessDurationDays is null or <= 0)
            issues.Add("Set the number of access days for limited access.");

        var lessons = course.Chapters.Where(c => !c.IsDeleted).SelectMany(c => c.Lessons.Where(l => !l.IsDeleted)).ToList();
        if (lessons.Count == 0)
            issues.Add("Add at least one lesson.");

        foreach (var chapter in course.Chapters.Where(c => !c.IsDeleted && !c.Lessons.Any(l => !l.IsDeleted)))
            issues.Add($"Chapter \"{chapter.Title}\" has no lessons.");

        foreach (var lesson in lessons)
        {
            if (lesson.Type == LessonType.Video && string.IsNullOrWhiteSpace(lesson.VideoUrl))
                issues.Add($"Lesson \"{lesson.Title}\" needs a video.");
            if (lesson.Type == LessonType.Article && string.IsNullOrWhiteSpace(lesson.Content))
                issues.Add($"Lesson \"{lesson.Title}\" needs article content.");
        }

        return issues;
    }
}
