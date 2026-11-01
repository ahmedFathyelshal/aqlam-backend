using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Mapster;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class CourseService : ICourseService
{
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
        var course = await _context.Courses
            .Include(c => c.Chapters)
                .ThenInclude(ch => ch.Lessons)
                    .ThenInclude(l => l.Resources)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (course == null)
            return null;

        return course.Adapt<CourseDraftDto>();
    }

    public async Task UpdateIdentityAsync(Guid id, string title, int? stageId, int? gradeId, int? subjectId, CancellationToken cancellationToken)
    {
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course == null) return;

        course.Title = title;
        course.StageId = stageId;
        course.GradeId = gradeId;
        course.SubjectId = subjectId;

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

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateMediaAsync(Guid id, string? subtitle, string? thumbnailUrl, string? promoVideoUrl, CancellationToken cancellationToken)
    {
        var course = await _context.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course == null) return;

        course.Subtitle = subtitle;
        course.ThumbnailUrl = thumbnailUrl;
        course.PromoVideoUrl = promoVideoUrl;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCurriculumAsync(Guid id, string? syllabusRequirements, ICollection<CourseChapterDto> chapters, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        // Step 1: Bulk-delete existing children directly in the database (child-first for FK order).
        await _context.Set<LessonResource>()
            .Where(r => r.Lesson.Chapter.CourseId == id)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.Set<CourseLesson>()
            .Where(l => l.Chapter.CourseId == id)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.Set<CourseChapter>()
            .Where(c => c.CourseId == id)
            .ExecuteDeleteAsync(cancellationToken);

        // Step 2: Update course property directly in the database (no entity load needed).
        await _context.Courses
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.SyllabusRequirements, syllabusRequirements)
                .SetProperty(c => c.LastModifiedAtUtc, DateTime.UtcNow),
            cancellationToken);

        // Step 3: Add new chapters directly to the DbSet (not via navigation property).
        foreach (var chapterDto in chapters)
        {
            var chapter = new CourseChapter
            {
                Title = chapterDto.Title,
                Order = chapterDto.Order,
                CourseId = id
            };

            foreach (var lessonDto in chapterDto.Lessons)
            {
                if (!Enum.TryParse<LessonType>(lessonDto.Type, ignoreCase: true, out var lessonType))
                {
                    lessonType = LessonType.Video;
                }

                var lesson = new CourseLesson
                {
                    Title = lessonDto.Title,
                    Order = lessonDto.Order,
                    IsPublished = lessonDto.IsPublished,
                    Type = lessonType,
                    DurationInMinutes = lessonDto.DurationInMinutes,
                    IsFreePreview = lessonDto.IsFreePreview,
                    VideoUrl = lessonDto.VideoUrl,
                    Summary = lessonDto.Summary,
                    Content = lessonDto.Content,
                    UnlockDaysAfterEnrollment = lessonDto.UnlockDaysAfterEnrollment,
                    RequirePreviousLessonCompletion = lessonDto.RequirePreviousLessonCompletion
                };

                foreach (var resourceDto in lessonDto.Resources)
                {
                    if (!Enum.TryParse<ResourceType>(resourceDto.Type, ignoreCase: true, out var resourceType))
                    {
                        resourceType = ResourceType.File;
                    }

                    lesson.Resources.Add(new LessonResource
                    {
                        DisplayName = resourceDto.DisplayName,
                        ResourceUrl = resourceDto.ResourceUrl,
                        Type = resourceType,
                        IsDownloadable = resourceDto.IsDownloadable
                    });
                }

                chapter.Lessons.Add(lesson);
            }

            _context.Set<CourseChapter>().Add(chapter);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}



