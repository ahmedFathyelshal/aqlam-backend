namespace AqlamAcademy.Domain.Lookups;

public class EducationStage : LookupEntity
{
    public ICollection<EducationGrade> Grades { get; set; } = new List<EducationGrade>();
}
