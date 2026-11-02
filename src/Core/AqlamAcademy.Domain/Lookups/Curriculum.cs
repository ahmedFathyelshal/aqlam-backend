namespace AqlamAcademy.Domain.Lookups;

public enum CurriculumKind
{
    National = 1,
    International = 2,
    TestPrep = 3
}

/// <summary>
/// Top of the academic hierarchy: Curriculum → Stage → Grade. A school may teach several curricula
/// (e.g. British subjects plus mandatory national subjects).
/// </summary>
public class Curriculum : LookupEntity
{
    public string Code { get; set; } = string.Empty;

    /// <summary>ISO country code for national curricula; null means offered in every country.</summary>
    public string? CountryCode { get; set; }

    public CurriculumKind Kind { get; set; }

    public ICollection<EducationStage> Stages { get; set; } = new List<EducationStage>();
}
