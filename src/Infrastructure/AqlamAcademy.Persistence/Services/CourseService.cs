using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.Commands;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using AqlamAcademy.Domain.Common;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Mapster;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class CourseService : ICourseService
{
    private const string ConcurrentEditMessage = "The curriculum was changed by someone else. Reload to see the latest version.";

    private readonly TenantDbContext _context;

    public CourseService(TenantDbContext context)
    {
        _context = context;
    }

    public async Task<CourseDraftDto> CreateDraftAsync(Course course, CancellationToken cancellationToken)
    {
        _context.Courses.Add(course);
        await _context.SaveChangesAsync(cancellationToken);

        return course.Adapt<CourseDraftDto>();
    }

    public async Task<Course?> GetCourseByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<CourseDraftDto?> GetDraftByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var course = await LoadTreeAsync(id, tracking: false, cancellationToken);
        return course?.Adapt<CourseDraftDto>();
    }

    public async Task<IReadOnlyList<CourseSummaryDto>> ListAsync(CancellationToken cancellationToken) =>
        await _context.Courses.AsNoTracking()
            .OrderByDescending(c => c.LastModifiedAtUtc ?? c.CreatedAtUtc)
            .Select(c => new CourseSummaryDto(
                c.Id,
                c.Title,
                c.Status.ToString(),
                c.Visibility.ToString(),
                c.CurriculumId,
                c.GradeId,
                c.SubjectId,
                c.Chapters.Count(),
                c.Chapters.SelectMany(ch => ch.Lessons).Count(),
                c.CreatedAtUtc,
                c.LastModifiedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task UpdateIdentityAsync(Guid id, string title, int? curriculumId, int? stageId, int? gradeId, int? subjectId, CancellationToken cancellationToken)
    {
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course == null) return;

        course.Title = title;
        course.CurriculumId = curriculumId;
        course.StageId = stageId;
        course.GradeId = gradeId;
        course.SubjectId = subjectId;
        course.LastModifiedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCommercialsAsync(Guid id, PricingType? pricingType, decimal? price, bool codeActivationOnly, AccessExpiryPolicy? expiryPolicy, int? accessDurationDays, CancellationToken cancellationToken)
    {
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course == null) return;

        course.PricingType = pricingType;
        course.Price = price;
        course.CodeActivationOnly = codeActivationOnly;
        course.AccessExpiryPolicy = expiryPolicy;
        course.AccessDurationDays = accessDurationDays;
        course.LastModifiedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateMediaAsync(Guid id, string? subtitle, string? thumbnailUrl, string? promoVideoUrl, CancellationToken cancellationToken)
    {
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course == null) return;

        course.Subtitle = subtitle;
        course.ThumbnailUrl = thumbnailUrl;
        course.PromoVideoUrl = promoVideoUrl;
        course.LastModifiedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<CourseDraftDto>> SyncCurriculumAsync(SyncCurriculumCommand command, CancellationToken cancellationToken)
    {
        var course = await LoadTreeAsync(command.Id, tracking: true, cancellationToken);
        if (course is null)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        if (command.ExpectedVersion is { } expected && expected != course.CurriculumVersion)
            return Result<CourseDraftDto>.Conflict(ConcurrentEditMessage);

        var chaptersById = course.Chapters.ToDictionary(c => c.Id);
        var lessonsById = course.Chapters.SelectMany(c => c.Lessons).ToDictionary(l => l.Id);

        // Validate every referenced id before changing anything, so a bad request never half-applies.
        var unknownIds = FindUnknownIds(command, chaptersById, lessonsById);
        if (unknownIds.Count > 0)
            return Result<CourseDraftDto>.Failure(unknownIds);

        var now = DateTime.UtcNow;
        var keptChapterIds = new HashSet<Guid>();
        var keptLessonIds = new HashSet<Guid>();

        for (var chapterIndex = 0; chapterIndex < command.Chapters.Count; chapterIndex++)
        {
            var chapterInput = command.Chapters[chapterIndex];
            var chapter = chapterInput.Id is { } chapterId ? chaptersById[chapterId] : AddChapter(course);

            chapter.Title = chapterInput.Title.Trim();
            chapter.Order = chapterIndex;
            chapter.LastModifiedAtUtc = now;
            keptChapterIds.Add(chapter.Id);

            if (chapterInput.Lessons is null)
            {
                // "Leave unchanged": keep the chapter's current lessons.
                keptLessonIds.UnionWith(chapter.Lessons.Select(l => l.Id));
                continue;
            }

            for (var lessonIndex = 0; lessonIndex < chapterInput.Lessons.Count; lessonIndex++)
            {
                var lessonInput = chapterInput.Lessons[lessonIndex];
                var lesson = lessonInput.Id is { } lessonId ? lessonsById[lessonId] : new CourseLesson();

                if (lessonInput.Id is null)
                {
                    chapter.Lessons.Add(lesson);
                    _context.CourseLessons.Add(lesson); // explicit: the key is pre-set, so EF must not assume it exists
                }
                else if (lesson.ChapterId != chapter.Id)
                {
                    // Moved to another chapter: same lesson, same id (progress stays attached).
                    lesson.Chapter.Lessons.Remove(lesson);
                    lesson.Chapter = chapter;
                    chapter.Lessons.Add(lesson);
                }

                ApplyLesson(lesson, lessonInput, lessonIndex, now);
                keptLessonIds.Add(lesson.Id);
            }
        }

        // Anything no longer in the tree is soft-deleted (history such as progress keeps its references).
        foreach (var chapter in course.Chapters.Where(c => !keptChapterIds.Contains(c.Id)).ToList())
            MarkDeleted(chapter, now);
        foreach (var lesson in course.Chapters.SelectMany(c => c.Lessons).Where(l => !keptLessonIds.Contains(l.Id)).ToList())
        {
            MarkDeleted(lesson, now);
            foreach (var resource in lesson.Resources)
                MarkDeleted(resource, now);
        }

        course.SyllabusRequirements = command.SyllabusRequirements;
        course.CurriculumVersion++;
        course.LastModifiedAtUtc = now;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CourseDraftDto>.Conflict(ConcurrentEditMessage);
        }

        _context.ChangeTracker.Clear();
        return Result<CourseDraftDto>.Success((await GetDraftByIdAsync(course.Id, cancellationToken))!);
    }

    public async Task<IReadOnlyList<string>?> GetPublishIssuesAsync(Guid id, CancellationToken cancellationToken)
    {
        var course = await LoadTreeAsync(id, tracking: false, cancellationToken);
        return course is null ? null : [.. CourseLifecycle.GetPublishIssues(course), .. await QuizIssuesAsync(course, cancellationToken)];
    }

    public async Task<Result<CourseDraftDto>> ChangeStatusAsync(Guid id, CourseStatus status, CancellationToken cancellationToken)
    {
        var course = await LoadTreeAsync(id, tracking: true, cancellationToken);
        if (course is null)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        if (course.Status == status)
            return Result<CourseDraftDto>.Success(course.Adapt<CourseDraftDto>());

        if (!CourseLifecycle.CanTransition(course.Status, status))
            return Result<CourseDraftDto>.Conflict($"A {course.Status} course cannot be moved to {status}.");

        if (CourseLifecycle.RequiresCompleteCourse(status))
        {
            List<string> issues = [.. CourseLifecycle.GetPublishIssues(course), .. await QuizIssuesAsync(course, cancellationToken)];
            if (issues.Count > 0)
                return Result<CourseDraftDto>.Failure(issues);
        }

        course.Status = status;
        course.LastModifiedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return Result<CourseDraftDto>.Success(course.Adapt<CourseDraftDto>());
    }

    public async Task<Result<CourseDraftDto>> SetVisibilityAsync(Guid id, CourseVisibility visibility, CancellationToken cancellationToken)
    {
        var course = await LoadTreeAsync(id, tracking: true, cancellationToken);
        if (course is null)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        course.Visibility = visibility;
        course.LastModifiedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return Result<CourseDraftDto>.Success(course.Adapt<CourseDraftDto>());
    }

    /// <summary>Quiz lessons must have at least one question before the course can go live.</summary>
    private async Task<List<string>> QuizIssuesAsync(Course course, CancellationToken cancellationToken)
    {
        var quizLessons = course.Chapters.Where(c => !c.IsDeleted)
            .SelectMany(c => c.Lessons.Where(l => !l.IsDeleted && l.Type == LessonType.Quiz))
            .ToList();
        if (quizLessons.Count == 0)
            return [];

        var lessonIds = quizLessons.Select(l => l.Id).ToList();
        var ready = await _context.Quizzes
            .Where(q => lessonIds.Contains(q.LessonId) && q.Questions.Any())
            .Select(q => q.LessonId)
            .ToListAsync(cancellationToken);

        return quizLessons.Where(l => !ready.Contains(l.Id)).Select(l => $"Quiz \"{l.Title}\" has no questions.").ToList();
    }

    private Task<Course?> LoadTreeAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = _context.Courses
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Lessons)
                    .ThenInclude(l => l.Resources)
            .AsSplitQuery();

        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    private static List<string> FindUnknownIds(
        SyncCurriculumCommand command,
        Dictionary<Guid, CourseChapter> chaptersById,
        Dictionary<Guid, CourseLesson> lessonsById)
    {
        var errors = new List<string>();

        foreach (var chapter in command.Chapters)
        {
            if (chapter.Id is { } chapterId && !chaptersById.ContainsKey(chapterId))
                errors.Add($"Chapter {chapterId} does not belong to this course.");

            foreach (var lesson in chapter.Lessons ?? [])
            {
                if (lesson.Id is { } lessonId && !lessonsById.ContainsKey(lessonId))
                {
                    errors.Add($"Lesson {lessonId} does not belong to this course.");
                    continue;
                }

                var knownResources = lesson.Id is { } existingId
                    ? lessonsById[existingId].Resources.Select(r => r.Id).ToHashSet()
                    : [];
                foreach (var resource in lesson.Resources ?? [])
                {
                    if (resource.Id is { } resourceId && !knownResources.Contains(resourceId))
                        errors.Add($"Resource {resourceId} does not belong to lesson \"{lesson.Title}\".");
                }
            }
        }

        return errors;
    }

    private CourseChapter AddChapter(Course course)
    {
        var chapter = new CourseChapter { CourseId = course.Id };
        course.Chapters.Add(chapter);
        _context.CourseChapters.Add(chapter);
        return chapter;
    }

    private void ApplyLesson(CourseLesson lesson, SyncLessonInput input, int order, DateTime now)
    {
        lesson.Title = input.Title.Trim();
        lesson.Order = order;
        lesson.Type = Enum.Parse<LessonType>(input.Type, ignoreCase: true);
        lesson.DurationInMinutes = input.DurationInMinutes;
        lesson.IsFreePreview = input.IsFreePreview;
        lesson.IsPublished = input.IsPublished;
        lesson.VideoUrl = input.VideoUrl;
        lesson.Summary = input.Summary;
        lesson.Content = input.Content;
        lesson.UnlockDaysAfterEnrollment = input.UnlockDaysAfterEnrollment;
        lesson.RequirePreviousLessonCompletion = input.RequirePreviousLessonCompletion;
        lesson.LastModifiedAtUtc = now;

        if (input.Resources is null)
            return; // leave resources unchanged

        var existing = lesson.Resources.ToDictionary(r => r.Id);
        var kept = new HashSet<Guid>();
        foreach (var resourceInput in input.Resources)
        {
            var resource = resourceInput.Id is { } resourceId ? existing[resourceId] : new LessonResource();
            if (resourceInput.Id is null)
            {
                lesson.Resources.Add(resource);
                _context.LessonResources.Add(resource);
            }

            resource.DisplayName = resourceInput.DisplayName.Trim();
            resource.ResourceUrl = resourceInput.ResourceUrl.Trim();
            resource.Type = Enum.Parse<ResourceType>(resourceInput.Type, ignoreCase: true);
            resource.IsDownloadable = resourceInput.IsDownloadable;
            resource.LastModifiedAtUtc = now;
            kept.Add(resource.Id);
        }

        foreach (var removed in existing.Values.Where(r => !kept.Contains(r.Id)))
            MarkDeleted(removed, now);
    }

    private static void MarkDeleted(ISoftDelete entity, DateTime now)
    {
        entity.IsDeleted = true;
        entity.DeletedAtUtc = now;
    }
}
