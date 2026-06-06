namespace AqlamAcademy.Application.Common.Interfaces;

using AqlamAcademy.Application.Features.Lookups;

public interface ILookupService
{
    Task<IEnumerable<EducationStageDto>> GetEducationStagesAsync(CancellationToken cancellationToken);
    Task<IEnumerable<EducationGradeDto>> GetEducationGradesAsync(int? stageId, CancellationToken cancellationToken);
    Task<IEnumerable<SubjectDto>> GetSubjectsAsync(CancellationToken cancellationToken);
}
