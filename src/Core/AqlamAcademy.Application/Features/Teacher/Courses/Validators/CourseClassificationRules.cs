using AqlamAcademy.Application.Common.Interfaces;
using FluentValidation;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Validators;

/// <summary>
/// Courses live in the tenant store while lookups live in the platform store, so there are no database
/// foreign keys between them. These rules make sure the ids exist and are consistent with each other
/// (stage within the curriculum, grade within the stage). Missing values are allowed while drafting.
/// </summary>
public static class CourseClassificationRules
{
    public static void AddClassificationRules<T>(
        this AbstractValidator<T> validator,
        ILookupService lookups,
        Func<T, int?> curriculumId,
        Func<T, int?> stageId,
        Func<T, int?> gradeId,
        Func<T, int?> subjectId)
    {
        validator.RuleFor(x => x).CustomAsync(async (command, context, cancellationToken) =>
        {
            var curriculum = curriculumId(command);
            var stage = stageId(command);
            var grade = gradeId(command);
            var subject = subjectId(command);

            if (curriculum.HasValue && !(await lookups.GetCurriculaAsync(null, cancellationToken)).Any(c => c.Id == curriculum))
                context.AddFailure("curriculumId", "Unknown curriculum.");

            if (stage.HasValue)
            {
                var stageDto = (await lookups.GetEducationStagesAsync(null, cancellationToken)).FirstOrDefault(s => s.Id == stage);
                if (stageDto is null)
                    context.AddFailure("stageId", "Unknown educational stage.");
                else if (curriculum.HasValue && stageDto.CurriculumId != curriculum)
                    context.AddFailure("stageId", "This stage does not belong to the selected curriculum.");
            }

            if (grade.HasValue)
            {
                var gradeDto = (await lookups.GetEducationGradesAsync(null, cancellationToken)).FirstOrDefault(g => g.Id == grade);
                if (gradeDto is null)
                    context.AddFailure("gradeId", "Unknown grade.");
                else if (stage.HasValue && gradeDto.StageId != stage)
                    context.AddFailure("gradeId", "This grade does not belong to the selected stage.");
            }

            if (subject.HasValue && !(await lookups.GetSubjectsAsync(cancellationToken)).Any(s => s.Id == subject))
                context.AddFailure("subjectId", "Unknown subject.");
        });
    }
}

public class InitializeCourseDraftCommandValidator : AbstractValidator<Commands.InitializeCourseDraftCommand>
{
    public InitializeCourseDraftCommandValidator(ILookupService lookups)
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        this.AddClassificationRules(lookups, x => x.CurriculumId, x => x.StageId, x => x.GradeId, x => x.SubjectId);
    }
}

public class UpdateCourseIdentityCommandValidator : AbstractValidator<Commands.UpdateCourseIdentityCommand>
{
    public UpdateCourseIdentityCommandValidator(ILookupService lookups)
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        this.AddClassificationRules(lookups, x => x.CurriculumId, x => x.StageId, x => x.GradeId, x => x.SubjectId);
    }
}
