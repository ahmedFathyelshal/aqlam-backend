namespace AqlamAcademy.Application.Common.Interfaces;

using AqlamAcademy.Application.Features.Lookups;

public interface ILookupService
{
    /// <summary>National curricula of the given country plus international ones; all curricula when no country is given.</summary>
    Task<IEnumerable<CurriculumDto>> GetCurriculaAsync(string? countryCode, CancellationToken cancellationToken);
    Task<IEnumerable<EducationStageDto>> GetEducationStagesAsync(int? curriculumId, CancellationToken cancellationToken);
    Task<IEnumerable<EducationGradeDto>> GetEducationGradesAsync(int? stageId, CancellationToken cancellationToken);
    Task<IEnumerable<SubjectDto>> GetSubjectsAsync(CancellationToken cancellationToken);
    Task<IEnumerable<CourseStateDto>> GetCourseStatesAsync(CancellationToken cancellationToken);
}
