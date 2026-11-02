namespace AqlamAcademy.Application.Features.Lookups;

public record CurriculumDto(int Id, string Code, string NameEn, string NameAr, string? CountryCode, string Kind, int OrderIndex);
public record EducationStageDto(int Id, int? CurriculumId, string NameEn, string NameAr, int OrderIndex);
public record EducationGradeDto(int Id, int StageId, string NameEn, string NameAr, int OrderIndex);
public record SubjectDto(int Id, string NameEn, string NameAr, int OrderIndex);
public record CourseStateDto(int Id, string NameEn, string NameAr, int OrderIndex);
