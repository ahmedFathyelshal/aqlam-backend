using System.Text;
using System.Text.RegularExpressions;

namespace AqlamAcademy.Domain.Assessments;

/// <summary>Grades attempt items. Pure functions: the same answers always get the same score.</summary>
public static partial class QuizGrading
{
    /// <summary>
    /// Choice questions: full points only for exactly the correct set of options (no partial credit yet).
    /// Short answers: compared after normalisation against every accepted answer.
    /// </summary>
    public static void GradeItem(AttemptItem item)
    {
        var correct = item.Type switch
        {
            QuestionType.ShortAnswer => item.TextAnswer is { } text
                && item.AcceptedAnswers.Any(accepted => NormalizeAnswer(accepted) == NormalizeAnswer(text)),
            _ => item.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet().SetEquals(item.SelectedOptionIds)
        };

        item.IsCorrect = correct;
        item.AwardedPoints = correct ? item.Points : 0;
    }

    public static void GradeAttempt(QuizAttempt attempt, int passPercent)
    {
        foreach (var item in attempt.Items)
            GradeItem(item);

        attempt.Score = attempt.Items.Sum(i => i.AwardedPoints);
        attempt.MaxScore = attempt.Items.Sum(i => i.Points);
        attempt.Percent = attempt.MaxScore == 0 ? 0 : Math.Round(attempt.Score * 100m / attempt.MaxScore, 2);
        attempt.Passed = attempt.Percent >= passPercent;
    }

    /// <summary>
    /// Case, spacing and Arabic spelling variants should not fail an otherwise correct answer:
    /// removes diacritics/tatweel, unifies alef forms, taa marbuta → haa, alef maqsura → yaa,
    /// and maps Arabic-Indic digits to Latin.
    /// </summary>
    public static string NormalizeAnswer(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        text = Diacritics().Replace(text, string.Empty);

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                _ => ch
            });
        }

        text = builder.ToString().Normalize(NormalizationForm.FormC);
        return Spaces().Replace(text, " ");
    }

    [GeneratedRegex(@"[ً-ٰٟـ]")]
    private static partial Regex Diacritics();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
