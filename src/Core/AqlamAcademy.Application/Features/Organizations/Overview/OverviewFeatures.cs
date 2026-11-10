using AqlamAcademy.Application.Common.Models;
using MediatR;

namespace AqlamAcademy.Application.Features.Organizations.Overview;

/// <summary>Where a class stands with today's attendance.</summary>
public enum TodayStatus
{
    /// <summary>The class does not meet today (weekend, holiday, no group meeting).</summary>
    NotToday = 0,
    /// <summary>Expected later today; not late yet.</summary>
    Pending = 1,
    /// <summary>Expected by now and not taken.</summary>
    Missing = 2,
    Taken = 3
}

public record OverviewRulesDto(int FollowUpAbsentDays, int FollowUpLateDays, int GoodGradePercent, int LowGradePercent,
    TimeOnly AttendanceDueBy, int GroupAttendanceGraceMinutes);

public record OverviewSchoolDto(string Name, string? LogoUrl, string Type, int Students, int Teachers, int Classes, OverviewRulesDto Rules);

/// <summary>Today in the organization's time zone. LastSchoolDay is set when today is not a school day.</summary>
public record OverviewTodayDto(
    DateOnly Date,
    bool IsSchoolDay,
    int Expected,
    int Taken,
    int Present,
    int Absent,
    int Late,
    int Excused,
    int? RatePercent,
    int? UsualRatePercent,
    OverviewDayDto? LastSchoolDay);

/// <summary>One school day: attended (present or late) out of everyone marked, excused left out.</summary>
public record OverviewDayDto(DateOnly Date, int RatePercent, int Marked);

public record OverviewClassDto(
    Guid Id,
    string Name,
    string Kind,
    int Students,
    string? HomeroomTeacherName,
    TodayStatus Today,
    TimeOnly? StartsAt,
    int? AttendancePercent,
    int? AveragePercent,
    int FollowUp);

public record OverviewFollowUpDto(Guid UserId, string FullName, Guid? ClassId, string? ClassName, int Absent, int Late, int? AveragePercent);

public record OverviewGradingDto(Guid CourseId, string CourseTitle, int Waiting, DateTime OldestSubmittedAtUtc);

/// <summary>The owner's morning view of the organization: today, classes, students to follow up and homework to grade.</summary>
public record OrganizationOverviewDto(
    OverviewSchoolDto School,
    OverviewTodayDto Today,
    IReadOnlyList<OverviewDayDto> Days,
    IReadOnlyList<OverviewClassDto> Classes,
    int FollowUpTotal,
    IReadOnlyList<OverviewFollowUpDto> FollowUp,
    IReadOnlyList<OverviewGradingDto> Grading);

public interface IOrganizationOverviewService
{
    /// <param name="mine">Only the classes the user teaches or leads, and homework in their own courses (a teacher's day).</param>
    Task<Result<OrganizationOverviewDto>> GetAsync(bool mine, CancellationToken cancellationToken);
}

public record GetOrganizationOverviewQuery(bool Mine = false) : IRequest<Result<OrganizationOverviewDto>>;

public class GetOrganizationOverviewQueryHandler(IOrganizationOverviewService overview) : IRequestHandler<GetOrganizationOverviewQuery, Result<OrganizationOverviewDto>>
{
    public Task<Result<OrganizationOverviewDto>> Handle(GetOrganizationOverviewQuery request, CancellationToken cancellationToken) =>
        overview.GetAsync(request.Mine, cancellationToken);
}

/// <summary>Rules for "is attendance due today", kept apart from the queries so they read in one place.</summary>
public static class OverviewRules
{
    /// <summary>
    /// A school day is a weekday on which the organization took attendance in the last four weeks. A new
    /// organization with no history yet assumes the two days before its week start are the weekend.
    /// </summary>
    public static bool IsSchoolDay(DateOnly day, IReadOnlyCollection<DayOfWeek> recentAttendanceDays, DayOfWeek weekStart)
    {
        if (recentAttendanceDays.Count > 0)
            return recentAttendanceDays.Contains(day.DayOfWeek);
        var weekendEnd = (DayOfWeek)(((int)weekStart + 6) % 7);
        var weekendStart = (DayOfWeek)(((int)weekStart + 5) % 7);
        return day.DayOfWeek != weekendStart && day.DayOfWeek != weekendEnd;
    }

    /// <summary>Status of a school section (one daily sheet): late after the school's own due time.</summary>
    public static TodayStatus Section(bool schoolDay, bool taken, TimeOnly now, TimeOnly dueBy) =>
        taken ? TodayStatus.Taken
        : !schoolDay ? TodayStatus.NotToday
        : now >= dueBy ? TodayStatus.Missing
        : TodayStatus.Pending;

    /// <summary>Status of a center group: due only on the days it meets, late after the first meeting starts plus the school's grace.</summary>
    public static TodayStatus Group(TimeOnly? firstStartToday, bool taken, TimeOnly now, int graceMinutes) =>
        taken ? TodayStatus.Taken
        : firstStartToday is not { } start ? TodayStatus.NotToday
        : now.ToTimeSpan() >= start.ToTimeSpan() + TimeSpan.FromMinutes(graceMinutes) ? TodayStatus.Missing // no wrap past midnight
        : TodayStatus.Pending;

    /// <summary>Attended (present or late) out of everyone marked, leaving excused absences out. Null when nobody counts.</summary>
    public static int? Rate(int present, int late, int absent) =>
        present + late + absent == 0 ? null : (int)Math.Round((present + late) * 100.0 / (present + late + absent));
}
