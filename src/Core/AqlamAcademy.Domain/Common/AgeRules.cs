namespace AqlamAcademy.Domain.Common;

/// <summary>
/// Age rules agreed on 2026-11-12. Only the birth year is kept, so ages are by calendar year.
/// Under 13: a parent creates the account. 13 to 17: may register and study, but a linked parent pays.
/// </summary>
public static class AgeRules
{
    public const int MinSelfRegistrationAge = 13;
    public const int AdultAge = 18;

    public static int Age(int birthYear, DateTime now) => now.Year - birthYear;

    public static bool IsValidBirthYear(int birthYear, DateTime now) => birthYear >= 1900 && birthYear <= now.Year;

    public static bool MaySelfRegister(int birthYear, DateTime now) => Age(birthYear, now) >= MinSelfRegistrationAge;

    /// <summary>
    /// Whether a learner may pay by themselves. Accounts with no birth year that are students (imported by a school,
    /// or registered before the rule) are treated as minors.
    /// </summary>
    public static bool MayPay(int? birthYear, bool isStudent, DateTime now) =>
        birthYear is { } year ? Age(year, now) >= AdultAge : !isStudent;
}
