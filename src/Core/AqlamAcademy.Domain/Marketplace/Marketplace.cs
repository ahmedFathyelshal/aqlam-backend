using System.Text;
using AqlamAcademy.Domain.Assessments;

namespace AqlamAcademy.Domain.Marketplace;

/// <summary>
/// A public, published (or scheduled) course as the marketplace sees it. Courses live in tenant stores (some in a
/// school's own database), so search reads this copy in the platform store instead. Rewritten whenever the course,
/// its organization or its teacher changes; removed when the course stops being public.
/// </summary>
public class MarketplaceCourse
{
    /// <summary>Same as the course id.</summary>
    public Guid CourseId { get; set; }
    /// <summary>Address of the public course page: the title in words, then <see cref="ShortCode"/>.</summary>
    public string PublicSlug { get; set; } = string.Empty;
    /// <summary>Never changes, so an old link still finds the course after its title (and slug) changed.</summary>
    public string ShortCode { get; set; } = string.Empty;

    public Guid OrganizationId { get; set; }
    public string OrganizationSlug { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    /// <summary>Personal (an independent teacher) or Center.</summary>
    public Organizations.OrganizationType OrganizationType { get; set; }
    public string CountryCode { get; set; } = "SA";
    public string Currency { get; set; } = "SAR";

    /// <summary>The independent teacher, or the center staff member who created the course.</summary>
    public Guid TeacherUserId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public Classes.Gender? TeacherGender { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool HasPromoVideo { get; set; }

    public int? CurriculumId { get; set; }
    public int? StageId { get; set; }
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }

    public bool IsFree { get; set; }
    public decimal? Price { get; set; }
    public bool CodeActivationOnly { get; set; }
    /// <summary>Scheduled courses are shown as "coming soon" and cannot be joined yet.</summary>
    public bool IsComingSoon { get; set; }

    public int LessonCount { get; set; }
    public int TotalMinutes { get; set; }
    public int FreePreviewCount { get; set; }

    /// <summary>Normalized title, subtitle and subject words (see <see cref="SearchText"/>).</summary>
    public string SearchText { get; set; } = string.Empty;
    /// <summary>Normalized teacher and organization names, kept apart so a rename updates only this.</summary>
    public string ProviderSearchText { get; set; } = string.Empty;

    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>An independent teacher who chose to appear in search. Students never get a row here.</summary>
public class MarketplaceTeacher
{
    /// <summary>The teacher's personal academy.</summary>
    public Guid OrganizationId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public Guid TeacherUserId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Headline { get; set; }
    public string? City { get; set; }
    public string CountryCode { get; set; } = "SA";
    public Classes.Gender Gender { get; set; }
    public string? PhotoUrl { get; set; }

    /// <summary>Taken from the teacher's public courses.</summary>
    public int[] SubjectIds { get; set; } = [];
    public int[] GradeIds { get; set; } = [];
    public int[] CurriculumIds { get; set; } = [];
    public int CourseCount { get; set; }

    public string SearchText { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A teacher picked by the platform (story 3.5.4) while real rankings do not exist. Always shown as a pick by
/// أقلام, never as a ranking. A null subject means a general pick.
/// </summary>
public class FeaturedTeacher
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public int? SubjectId { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Search matching that ignores case, diacritics and common Arabic spelling variants.</summary>
public static class SearchText
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var text = QuizGrading.NormalizeAnswer(value);
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => ch
            });
        }
        return builder.ToString();
    }

    /// <summary>Normalized words of a query, without the Arabic definite article so "الرياضيات" finds "رياضيات".</summary>
    public static IReadOnlyList<string> Terms(string? query) =>
        Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(StripArticle)
            .Where(t => t.Length > 0)
            .Distinct()
            .Take(8)
            .ToList();

    /// <summary>Builds the stored text: every word with and without the definite article.</summary>
    public static string Build(params string?[] parts)
    {
        var words = parts.SelectMany(p => Normalize(p).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .SelectMany(w => new[] { w, StripArticle(w) })
            .Where(w => w.Length > 0)
            .Distinct();
        return " " + string.Join(' ', words) + " ";
    }

    private static string StripArticle(string word) =>
        word.Length > 3 && word.StartsWith("ال", StringComparison.Ordinal) ? word[2..] : word;
}
