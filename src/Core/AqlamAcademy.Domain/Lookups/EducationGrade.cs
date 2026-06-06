namespace AqlamAcademy.Domain.Lookups;

public class EducationGrade : LookupEntity
{
    public int EducationStageId { get; set; }
    public EducationStage Stage { get; set; } = null!;
}
