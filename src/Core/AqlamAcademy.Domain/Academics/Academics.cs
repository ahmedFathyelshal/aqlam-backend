using AqlamAcademy.Domain.Common;

namespace AqlamAcademy.Domain.Academics;

/// <summary>A school year of one organization; exactly one year is current at a time.</summary>
public class AcademicYear : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }

    public List<AcademicTerm> Terms { get; set; } = [];
}

/// <summary>A semester or term. Report cards reach families only after the school publishes the term.</summary>
public class AcademicTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AcademicYearId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool ReportCardsPublished { get; set; }
}

public enum GradeSource
{
    Manual = 1,
    Quiz = 2,
    Assignment = 3
}

/// <summary>A weighted part of a subject's grade in one term (e.g. homework 20%, final exam 40%).</summary>
public class GradeCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeachingAssignmentId { get; set; }
    public Guid TermId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal WeightPercent { get; set; }
    public int Order { get; set; }

    public List<GradeItem> Items { get; set; } = [];
}

/// <summary>One graded thing. Quiz and assignment items read their scores from the course lesson.</summary>
public class GradeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal MaxPoints { get; set; }
    public GradeSource Source { get; set; } = GradeSource.Manual;
    public Guid? SourceLessonId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<GradeEntry> Entries { get; set; } = [];
}

/// <summary>A score typed by the teacher; on a linked item it overrides the score from the lesson.</summary>
public class GradeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public Guid UserId { get; set; }
    public decimal Score { get; set; }
}

public enum GradingScale
{
    Percent = 1,
    Letters = 2,
    Descriptive = 3 // ممتاز، جيد جدًا، جيد، مقبول، ضعيف
}

public static class GradeCalculator
{
    /// <summary>Percent of a category over the items that have a score; ungraded items do not count against the student.</summary>
    public static decimal? CategoryPercent(IEnumerable<(decimal? Score, decimal Max)> items)
    {
        var scored = items.Where(i => i.Score is not null && i.Max > 0).ToList();
        return scored.Count == 0 ? null : Round(scored.Sum(i => i.Score!.Value) * 100 / scored.Sum(i => i.Max));
    }

    /// <summary>Weighted average over the categories that have a percent, normalised by their weights.</summary>
    public static decimal? FinalPercent(IEnumerable<(decimal Weight, decimal? Percent)> categories)
    {
        var graded = categories.Where(c => c.Percent is not null && c.Weight > 0).ToList();
        var weights = graded.Sum(c => c.Weight);
        return weights == 0 ? null : Round(graded.Sum(c => c.Weight * c.Percent!.Value) / weights);
    }

    /// <summary>Label of a percent on a scale: a translation code on the client, null for plain percentages.</summary>
    public static string? Label(decimal? percent, GradingScale scale)
    {
        if (percent is not { } p || scale == GradingScale.Percent)
            return null;
        var band = p >= 90 ? 0 : p >= 80 ? 1 : p >= 70 ? 2 : p >= 60 ? 3 : 4;
        return scale == GradingScale.Letters
            ? new[] { "A", "B", "C", "D", "F" }[band]
            : new[] { "Excellent", "VeryGood", "Good", "Pass", "Fail" }[band];
    }

    private static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
