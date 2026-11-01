using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using AqlamAcademy.Domain.Enums;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Commands;

/// <summary>
/// Saves the curriculum tree without destroying identities: items with an id are updated in place,
/// items without an id are created, items missing from the tree are soft-deleted, and positions become the
/// order. A null Lessons / Resources list means "leave unchanged" (the skeleton step edits chapters only).
/// </summary>
public record SyncCurriculumCommand(
    Guid Id,
    int? ExpectedVersion,
    string? SyllabusRequirements,
    IReadOnlyList<SyncChapterInput> Chapters) : IRequest<Result<CourseDraftDto>>;

public record SyncChapterInput(Guid? Id, string Title, IReadOnlyList<SyncLessonInput>? Lessons);

public record SyncLessonInput(
    Guid? Id,
    string Title,
    string Type,
    int? DurationInMinutes,
    bool IsFreePreview,
    bool IsPublished,
    string? VideoUrl,
    string? Summary,
    string? Content,
    int UnlockDaysAfterEnrollment,
    bool RequirePreviousLessonCompletion,
    IReadOnlyList<SyncResourceInput>? Resources);

public record SyncResourceInput(Guid? Id, string DisplayName, string ResourceUrl, string Type, bool IsDownloadable = true);

public class SyncCurriculumCommandValidator : AbstractValidator<SyncCurriculumCommand>
{
    public const int MaxChapters = 100;
    public const int MaxLessonsPerChapter = 200;

    public SyncCurriculumCommandValidator()
    {
        RuleFor(x => x.SyllabusRequirements).MaximumLength(4000);
        RuleFor(x => x.Chapters).NotNull().Must(c => c.Count <= MaxChapters).WithMessage($"A course can have at most {MaxChapters} chapters.");
        RuleFor(x => x.Chapters)
            .Must(chapters => HaveUniqueIds(chapters.Select(c => c.Id)))
            .WithMessage("A chapter appears more than once.");
        RuleFor(x => x.Chapters)
            .Must(chapters => HaveUniqueIds(chapters.SelectMany(c => c.Lessons ?? []).Select(l => l.Id)))
            .WithMessage("A lesson appears more than once.");

        RuleForEach(x => x.Chapters).ChildRules(chapter =>
        {
            chapter.RuleFor(c => c.Title).NotEmpty().MaximumLength(150);
            chapter.RuleFor(c => c.Lessons).Must(l => l is null || l.Count <= MaxLessonsPerChapter)
                .WithMessage($"A chapter can have at most {MaxLessonsPerChapter} lessons.");

            chapter.RuleForEach(c => c.Lessons).ChildRules(lesson =>
            {
                lesson.RuleFor(l => l.Title).NotEmpty().MaximumLength(200);
                lesson.RuleFor(l => l.Type).Must(t => Enum.TryParse<LessonType>(t, true, out _))
                    .WithMessage("'{PropertyValue}' is not a valid lesson type.");
                lesson.RuleFor(l => l.DurationInMinutes).InclusiveBetween(0, 1440).When(l => l.DurationInMinutes.HasValue);
                lesson.RuleFor(l => l.UnlockDaysAfterEnrollment).InclusiveBetween(0, 3650);
                lesson.RuleFor(l => l.VideoUrl).MaximumLength(1000);
                lesson.RuleFor(l => l.Summary).MaximumLength(500);

                lesson.RuleForEach(l => l.Resources).ChildRules(resource =>
                {
                    resource.RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(150);
                    resource.RuleFor(r => r.ResourceUrl).NotEmpty().MaximumLength(2000);
                    resource.RuleFor(r => r.Type).Must(t => Enum.TryParse<ResourceType>(t, true, out _))
                        .WithMessage("'{PropertyValue}' is not a valid resource type.");
                });
            });
        });
    }

    private static bool HaveUniqueIds(IEnumerable<Guid?> ids)
    {
        var present = ids.Where(id => id.HasValue).Select(id => id!.Value).ToList();
        return present.Count == present.Distinct().Count();
    }
}

public class SyncCurriculumCommandHandler(ICourseService courseService) : IRequestHandler<SyncCurriculumCommand, Result<CourseDraftDto>>
{
    public Task<Result<CourseDraftDto>> Handle(SyncCurriculumCommand request, CancellationToken cancellationToken) =>
        courseService.SyncCurriculumAsync(request, cancellationToken);
}
