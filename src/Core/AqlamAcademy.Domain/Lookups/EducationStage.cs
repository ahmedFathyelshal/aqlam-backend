namespace AqlamAcademy.Domain.Lookups;

public class EducationStage : LookupEntity
{
    // Nullable only so the column could be added to existing data; every seeded stage has a curriculum.
    public int? CurriculumId { get; set; }
    public Curriculum? Curriculum { get; set; }

    public ICollection<EducationGrade> Grades { get; set; } = new List<EducationGrade>();
}
