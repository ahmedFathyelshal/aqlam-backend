namespace AqlamAcademy.Application.Features.Lookups;

public record EducationStageDto(int Id, string NameEn, string NameAr, int OrderIndex);
public record EducationGradeDto(int Id, int StageId, string NameEn, string NameAr, int OrderIndex);
public record SubjectDto(int Id, string NameEn, string NameAr, int OrderIndex);
