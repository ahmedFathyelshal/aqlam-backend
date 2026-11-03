using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Academics;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Academics;

// ---- Academic years -----------------------------------------------------------------------------------------

public record AcademicTermDto(Guid Id, string Name, int Order, DateOnly StartDate, DateOnly EndDate, bool ReportCardsPublished);

public record AcademicYearDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, bool IsCurrent, IReadOnlyList<AcademicTermDto> Terms);

/// <summary>A term in the year form; Id is null for a new term.</summary>
public record AcademicTermInput(Guid? Id, string Name, DateOnly StartDate, DateOnly EndDate);

public interface IAcademicYearService
{
    Task<IReadOnlyList<AcademicYearDto>> ListAsync(CancellationToken cancellationToken);
    Task<Result<AcademicYearDto>> SaveAsync(Guid? id, string name, DateOnly startDate, DateOnly endDate, IReadOnlyList<AcademicTermInput> terms, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<AcademicYearDto>> SetCurrentAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<AcademicTermDto>> SetReportCardsPublishedAsync(Guid termId, bool published, CancellationToken cancellationToken);
}

public record ListAcademicYearsQuery : IRequest<Result<IReadOnlyList<AcademicYearDto>>>;

public class ListAcademicYearsQueryHandler(IAcademicYearService years) : IRequestHandler<ListAcademicYearsQuery, Result<IReadOnlyList<AcademicYearDto>>>
{
    public async Task<Result<IReadOnlyList<AcademicYearDto>>> Handle(ListAcademicYearsQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<AcademicYearDto>>.Success(await years.ListAsync(cancellationToken));
}

public record SaveAcademicYearCommand(Guid? Id, string Name, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<AcademicTermInput>? Terms) : IRequest<Result<AcademicYearDto>>;

public class SaveAcademicYearCommandValidator : AbstractValidator<SaveAcademicYearCommand>
{
    public SaveAcademicYearCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate).WithMessage("The year must end after it starts.");
        RuleFor(x => x.Terms).NotEmpty().WithMessage("Add at least one term.").Must(t => t is null || t.Count <= 6).WithMessage("At most 6 terms.");
        RuleForEach(x => x.Terms).ChildRules(term =>
        {
            term.RuleFor(t => t.Name).NotEmpty().MaximumLength(100);
            term.RuleFor(t => t.EndDate).GreaterThan(t => t.StartDate).WithMessage("A term must end after it starts.");
        });
        RuleFor(x => x).Must(TermsFitTheYear).WithName("terms").WithMessage("Terms must be inside the year and must not overlap.");
    }

    private static bool TermsFitTheYear(SaveAcademicYearCommand command)
    {
        var terms = (command.Terms ?? []).OrderBy(t => t.StartDate).ToList();
        if (terms.Any(t => t.StartDate < command.StartDate || t.EndDate > command.EndDate))
            return false;
        return terms.Zip(terms.Skip(1)).All(pair => pair.First.EndDate < pair.Second.StartDate);
    }
}

public class SaveAcademicYearCommandHandler(IAcademicYearService years) : IRequestHandler<SaveAcademicYearCommand, Result<AcademicYearDto>>
{
    public Task<Result<AcademicYearDto>> Handle(SaveAcademicYearCommand r, CancellationToken cancellationToken) =>
        years.SaveAsync(r.Id, r.Name.Trim(), r.StartDate, r.EndDate, r.Terms ?? [], cancellationToken);
}

public record DeleteAcademicYearCommand(Guid Id) : IRequest<Result<bool>>;

public class DeleteAcademicYearCommandHandler(IAcademicYearService years) : IRequestHandler<DeleteAcademicYearCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(DeleteAcademicYearCommand r, CancellationToken cancellationToken) => years.DeleteAsync(r.Id, cancellationToken);
}

public record SetCurrentAcademicYearCommand(Guid Id) : IRequest<Result<AcademicYearDto>>;

public class SetCurrentAcademicYearCommandHandler(IAcademicYearService years) : IRequestHandler<SetCurrentAcademicYearCommand, Result<AcademicYearDto>>
{
    public Task<Result<AcademicYearDto>> Handle(SetCurrentAcademicYearCommand r, CancellationToken cancellationToken) => years.SetCurrentAsync(r.Id, cancellationToken);
}

public record SetReportCardsPublishedCommand(Guid TermId, bool Published) : IRequest<Result<AcademicTermDto>>;

public class SetReportCardsPublishedCommandHandler(IAcademicYearService years) : IRequestHandler<SetReportCardsPublishedCommand, Result<AcademicTermDto>>
{
    public Task<Result<AcademicTermDto>> Handle(SetReportCardsPublishedCommand r, CancellationToken cancellationToken) =>
        years.SetReportCardsPublishedAsync(r.TermId, r.Published, cancellationToken);
}

// ---- Gradebook ----------------------------------------------------------------------------------------------

public record GradeItemDto(Guid Id, string Title, decimal MaxPoints, string Source, Guid? SourceLessonId);

public record GradeCategoryDto(Guid Id, string Name, decimal WeightPercent, IReadOnlyList<GradeItemDto> Items);

/// <summary>A student's score on an item. FromLesson = the score comes from the quiz/homework, not typed.</summary>
public record GradeCellDto(Guid ItemId, decimal Score, bool FromLesson);

public record GradeRowDto(
    Guid UserId,
    string UserName,
    string FullName,
    IReadOnlyList<GradeCellDto> Cells,
    IReadOnlyDictionary<Guid, decimal?> CategoryPercents,
    decimal? FinalPercent,
    string? Label);

public record LinkableLessonDto(Guid LessonId, string Title, string Type);

public record GradebookDto(
    Guid ClassId,
    string ClassName,
    Guid AssignmentId,
    string SubjectName,
    Guid TermId,
    string TermName,
    string Scale,
    decimal WeightsTotal,
    IReadOnlyList<GradeCategoryDto> Categories,
    IReadOnlyList<GradeRowDto> Students,
    IReadOnlyList<LinkableLessonDto> LinkableLessons);

public record GradeScoreInput(Guid UserId, decimal? Score);

public record GradebookFile(byte[] Content, string FileName);

public interface IGradebookService
{
    Task<Result<GradebookDto>> GetAsync(Guid classId, Guid assignmentId, Guid termId, CancellationToken cancellationToken);
    Task<Result<GradeCategoryDto>> SaveCategoryAsync(Guid classId, Guid assignmentId, Guid? categoryId, Guid termId, string name, decimal weightPercent, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteCategoryAsync(Guid classId, Guid assignmentId, Guid categoryId, CancellationToken cancellationToken);
    Task<Result<GradeItemDto>> SaveItemAsync(Guid classId, Guid assignmentId, Guid categoryId, Guid? itemId, string title, decimal maxPoints, Guid? sourceLessonId, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteItemAsync(Guid classId, Guid assignmentId, Guid itemId, CancellationToken cancellationToken);
    Task<Result<bool>> SaveScoresAsync(Guid classId, Guid assignmentId, Guid itemId, IReadOnlyList<GradeScoreInput> scores, CancellationToken cancellationToken);
    Task<Result<GradebookFile>> ExportAsync(Guid classId, Guid assignmentId, Guid termId, CancellationToken cancellationToken);
}

public record GetGradebookQuery(Guid ClassId, Guid AssignmentId, Guid TermId) : IRequest<Result<GradebookDto>>;

public class GetGradebookQueryHandler(IGradebookService gradebook) : IRequestHandler<GetGradebookQuery, Result<GradebookDto>>
{
    public Task<Result<GradebookDto>> Handle(GetGradebookQuery r, CancellationToken cancellationToken) => gradebook.GetAsync(r.ClassId, r.AssignmentId, r.TermId, cancellationToken);
}

public record SaveGradeCategoryCommand(Guid ClassId, Guid AssignmentId, Guid? CategoryId, Guid TermId, string Name, decimal WeightPercent) : IRequest<Result<GradeCategoryDto>>;

public class SaveGradeCategoryCommandValidator : AbstractValidator<SaveGradeCategoryCommand>
{
    public SaveGradeCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WeightPercent).GreaterThan(0).LessThanOrEqualTo(100);
    }
}

public class SaveGradeCategoryCommandHandler(IGradebookService gradebook) : IRequestHandler<SaveGradeCategoryCommand, Result<GradeCategoryDto>>
{
    public Task<Result<GradeCategoryDto>> Handle(SaveGradeCategoryCommand r, CancellationToken cancellationToken) =>
        gradebook.SaveCategoryAsync(r.ClassId, r.AssignmentId, r.CategoryId, r.TermId, r.Name.Trim(), r.WeightPercent, cancellationToken);
}

public record DeleteGradeCategoryCommand(Guid ClassId, Guid AssignmentId, Guid CategoryId) : IRequest<Result<bool>>;

public class DeleteGradeCategoryCommandHandler(IGradebookService gradebook) : IRequestHandler<DeleteGradeCategoryCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(DeleteGradeCategoryCommand r, CancellationToken cancellationToken) => gradebook.DeleteCategoryAsync(r.ClassId, r.AssignmentId, r.CategoryId, cancellationToken);
}

public record SaveGradeItemCommand(Guid ClassId, Guid AssignmentId, Guid CategoryId, Guid? ItemId, string Title, decimal MaxPoints, Guid? SourceLessonId) : IRequest<Result<GradeItemDto>>;

public class SaveGradeItemCommandValidator : AbstractValidator<SaveGradeItemCommand>
{
    public SaveGradeItemCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MaxPoints).GreaterThan(0).LessThanOrEqualTo(1000);
    }
}

public class SaveGradeItemCommandHandler(IGradebookService gradebook) : IRequestHandler<SaveGradeItemCommand, Result<GradeItemDto>>
{
    public Task<Result<GradeItemDto>> Handle(SaveGradeItemCommand r, CancellationToken cancellationToken) =>
        gradebook.SaveItemAsync(r.ClassId, r.AssignmentId, r.CategoryId, r.ItemId, r.Title.Trim(), r.MaxPoints, r.SourceLessonId, cancellationToken);
}

public record DeleteGradeItemCommand(Guid ClassId, Guid AssignmentId, Guid ItemId) : IRequest<Result<bool>>;

public class DeleteGradeItemCommandHandler(IGradebookService gradebook) : IRequestHandler<DeleteGradeItemCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(DeleteGradeItemCommand r, CancellationToken cancellationToken) => gradebook.DeleteItemAsync(r.ClassId, r.AssignmentId, r.ItemId, cancellationToken);
}

public record SaveGradeScoresCommand(Guid ClassId, Guid AssignmentId, Guid ItemId, IReadOnlyList<GradeScoreInput> Scores) : IRequest<Result<bool>>;

public class SaveGradeScoresCommandValidator : AbstractValidator<SaveGradeScoresCommand>
{
    public SaveGradeScoresCommandValidator()
    {
        RuleFor(x => x.Scores).NotNull().Must(s => s.Count <= 1000);
        RuleForEach(x => x.Scores).ChildRules(s => s.RuleFor(i => i.Score).GreaterThanOrEqualTo(0).When(i => i.Score is not null));
    }
}

public class SaveGradeScoresCommandHandler(IGradebookService gradebook) : IRequestHandler<SaveGradeScoresCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(SaveGradeScoresCommand r, CancellationToken cancellationToken) =>
        gradebook.SaveScoresAsync(r.ClassId, r.AssignmentId, r.ItemId, r.Scores, cancellationToken);
}

public record ExportGradebookQuery(Guid ClassId, Guid AssignmentId, Guid TermId) : IRequest<Result<GradebookFile>>;

public class ExportGradebookQueryHandler(IGradebookService gradebook) : IRequestHandler<ExportGradebookQuery, Result<GradebookFile>>
{
    public Task<Result<GradebookFile>> Handle(ExportGradebookQuery r, CancellationToken cancellationToken) => gradebook.ExportAsync(r.ClassId, r.AssignmentId, r.TermId, cancellationToken);
}

// ---- Report cards -------------------------------------------------------------------------------------------

public record ReportCategoryDto(string Name, decimal WeightPercent, decimal? Percent);

public record ReportSubjectDto(string SubjectName, string? TeacherName, IReadOnlyList<ReportCategoryDto> Categories, decimal? FinalPercent, string? Label);

public record ReportAttendanceDto(int Present, int Absent, int Late, int Excused);

public record ReportCardDto(
    Guid UserId,
    string StudentName,
    string OrganizationName,
    string? LogoUrl,
    string YearName,
    Guid TermId,
    string TermName,
    DateOnly TermStart,
    DateOnly TermEnd,
    bool Published,
    IReadOnlyList<string> Classes,
    string Scale,
    IReadOnlyList<ReportSubjectDto> Subjects,
    decimal? OverallPercent,
    string? OverallLabel,
    ReportAttendanceDto Attendance);

public record PublishedTermDto(Guid TermId, string TermName, string YearName);

public interface IReportCardService
{
    /// <summary>Staff: any student and term (published or not).</summary>
    Task<Result<ReportCardDto>> ForStaffAsync(Guid studentId, Guid termId, CancellationToken cancellationToken);

    /// <summary>The student or a linked parent: published terms only.</summary>
    Task<Result<ReportCardDto>> ForFamilyAsync(Guid studentId, Guid termId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<PublishedTermDto>>> PublishedTermsAsync(Guid studentId, CancellationToken cancellationToken);
}

public record GetStaffReportCardQuery(Guid StudentId, Guid TermId) : IRequest<Result<ReportCardDto>>;

public class GetStaffReportCardQueryHandler(IReportCardService reports) : IRequestHandler<GetStaffReportCardQuery, Result<ReportCardDto>>
{
    public Task<Result<ReportCardDto>> Handle(GetStaffReportCardQuery r, CancellationToken cancellationToken) => reports.ForStaffAsync(r.StudentId, r.TermId, cancellationToken);
}

public record GetFamilyReportCardQuery(Guid StudentId, Guid TermId) : IRequest<Result<ReportCardDto>>;

public class GetFamilyReportCardQueryHandler(IReportCardService reports) : IRequestHandler<GetFamilyReportCardQuery, Result<ReportCardDto>>
{
    public Task<Result<ReportCardDto>> Handle(GetFamilyReportCardQuery r, CancellationToken cancellationToken) => reports.ForFamilyAsync(r.StudentId, r.TermId, cancellationToken);
}

public record GetPublishedTermsQuery(Guid StudentId) : IRequest<Result<IReadOnlyList<PublishedTermDto>>>;

public class GetPublishedTermsQueryHandler(IReportCardService reports) : IRequestHandler<GetPublishedTermsQuery, Result<IReadOnlyList<PublishedTermDto>>>
{
    public Task<Result<IReadOnlyList<PublishedTermDto>>> Handle(GetPublishedTermsQuery r, CancellationToken cancellationToken) => reports.PublishedTermsAsync(r.StudentId, cancellationToken);
}
