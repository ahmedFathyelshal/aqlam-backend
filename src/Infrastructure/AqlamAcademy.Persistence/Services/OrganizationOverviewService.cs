using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Organizations.Overview;
using AqlamAcademy.Domain.Assessments;
using AqlamAcademy.Domain.Attendance;
using AqlamAcademy.Domain.Classes;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// The owner's overview. Counts are aggregated in the database; only per-student totals and recent marks come back.
/// "Today" is the organization's own date and time, not the server's.
/// </summary>
public class OrganizationOverviewService : IOrganizationOverviewService
{
    private const int WindowDays = 30;
    private const int DaysShown = 20;
    private const int DaysLookBack = 60;
    private const int FollowUpShown = 20;

    private readonly ApplicationDbContext _platform;
    private readonly TenantDbContext _tenant;
    private readonly ICurrentOrganization _currentOrganization;
    private readonly ICurrentUserService _currentUser;

    public OrganizationOverviewService(ApplicationDbContext platform, TenantDbContext tenant, ICurrentOrganization currentOrganization, ICurrentUserService currentUser)
    {
        _platform = platform;
        _tenant = tenant;
        _currentOrganization = currentOrganization;
        _currentUser = currentUser;
    }

    public async Task<Result<OrganizationOverviewDto>> GetAsync(bool mine, CancellationToken cancellationToken)
    {
        var orgId = _currentOrganization.OrganizationId;
        var org = await _platform.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);
        if (org is null)
            return Result<OrganizationOverviewDto>.NotFound("Organization not found.");

        var now = LocalNow(org.TimeZone);
        var today = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        var windowStart = today.AddDays(-WindowDays);

        // Classes of the current academic year (and classes that predate years).
        var year = await _tenant.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.IsCurrent, cancellationToken);
        var classQuery = _tenant.ClassGroups.AsNoTracking();
        if (year is not null)
            classQuery = classQuery.Where(c => c.AcademicYearId == year.Id || c.AcademicYearId == null);
        var me = _currentUser.UserId;
        if (mine)
            classQuery = classQuery.Where(c => c.HomeroomTeacherUserId == me || c.Assignments.Any(a => a.TeacherUserId == me));
        var classes = await classQuery.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        var classIds = classes.Select(c => c.Id).ToList();

        var roster = await _tenant.ClassStudents.AsNoTracking().Where(s => classIds.Contains(s.ClassGroupId))
            .Select(s => new { s.ClassGroupId, s.UserId }).ToListAsync(cancellationToken);
        var courseOfClass = await _tenant.TeachingAssignments.AsNoTracking().Where(a => classIds.Contains(a.ClassGroupId))
            .Select(a => new { a.ClassGroupId, a.CourseId }).ToListAsync(cancellationToken);

        // Attendance ------------------------------------------------------------------------------------------
        var sessions = _tenant.AttendanceSessions.AsNoTracking().Where(s => classIds.Contains(s.ClassGroupId));
        var todaySessions = await sessions.Where(s => s.Date == today).Select(s => s.ClassGroupId).Distinct().ToListAsync(cancellationToken);
        var recentWeekdays = (await sessions.Where(s => s.Date >= today.AddDays(-28) && s.Date < today).Select(s => s.Date).Distinct().ToListAsync(cancellationToken))
            .Select(d => d.DayOfWeek).ToHashSet();

        var marks = from r in _tenant.AttendanceRecords.AsNoTracking()
                    join s in sessions on r.SessionId equals s.Id
                    select new { s.Date, s.ClassGroupId, r.UserId, r.Status };

        var byDay = await marks.Where(m => m.Date >= today.AddDays(-DaysLookBack) && m.Date <= today)
            .GroupBy(m => new { m.Date, m.Status }).Select(g => new { g.Key.Date, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var byClass = await marks.Where(m => m.Date > windowStart && m.Date <= today)
            .GroupBy(m => new { m.ClassGroupId, m.Status }).Select(g => new { g.Key.ClassGroupId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var byStudent = await marks.Where(m => m.Date > windowStart && m.Date <= today && (m.Status == AttendanceStatus.Absent || m.Status == AttendanceStatus.Late))
            .GroupBy(m => new { m.UserId, m.Status }).Select(g => new { g.Key.UserId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var days = byDay.GroupBy(d => d.Date).OrderBy(d => d.Key)
            .Select(d =>
            {
                int Count(AttendanceStatus status) => d.Where(x => x.Status == status).Sum(x => x.Count);
                var rate = OverviewRules.Rate(Count(AttendanceStatus.Present), Count(AttendanceStatus.Late), Count(AttendanceStatus.Absent));
                return rate is null ? null : new OverviewDayDto(d.Key, rate.Value, d.Sum(x => x.Count));
            })
            .OfType<OverviewDayDto>()
            .TakeLast(DaysShown)
            .ToList();

        int TodayCount(AttendanceStatus status) => byDay.Where(d => d.Date == today && d.Status == status).Sum(d => d.Count);
        var earlier = days.Where(d => d.Date < today).ToList();
        var inYear = year is null || (today >= year.StartDate && today <= year.EndDate);
        var schoolDay = inYear && OverviewRules.IsSchoolDay(today, recentWeekdays, org.WeekStartDay);

        // Grades: quiz attempts and graded homework of the last 30 days, as percents.
        var since = DateTime.UtcNow.AddDays(-WindowDays);
        var quizMarks = await _tenant.QuizAttempts.AsNoTracking()
            .Where(a => a.SubmittedAtUtc != null && a.SubmittedAtUtc >= since)
            .Select(a => new { a.UserId, a.CourseId, Percent = (double)a.Percent })
            .ToListAsync(cancellationToken);
        var homeworkMarks = await (
                from s in _tenant.AssignmentSubmissions.AsNoTracking()
                join a in _tenant.Assignments.AsNoTracking() on s.AssignmentId equals a.Id
                where s.Score != null && a.MaxPoints > 0 && s.GradedAtUtc >= since
                select new { s.UserId, s.CourseId, Percent = (double)(s.Score!.Value * 100 / a.MaxPoints) })
            .ToListAsync(cancellationToken);
        var gradeMarks = quizMarks.Concat(homeworkMarks).ToList();

        // Students: the school's own follow-up limits, the same ones the parent page uses.
        var studentIds = roster.Select(r => r.UserId).Distinct().ToList();
        var absences = byStudent.Where(s => s.Status == AttendanceStatus.Absent).ToDictionary(s => s.UserId, s => s.Count);
        var lates = byStudent.Where(s => s.Status == AttendanceStatus.Late).ToDictionary(s => s.UserId, s => s.Count);
        var averages = gradeMarks.Where(g => studentIds.Contains(g.UserId)).GroupBy(g => g.UserId)
            .ToDictionary(g => g.Key, g => (int)Math.Round(g.Average(x => x.Percent)));

        bool NeedsFollowUp(Guid id) =>
            absences.GetValueOrDefault(id) >= org.FollowUpAbsentDays
            || lates.GetValueOrDefault(id) >= org.FollowUpLateDays
            || (averages.TryGetValue(id, out var avg) && avg < org.LowGradePercent);
        var followUpIds = studentIds.Where(NeedsFollowUp).ToHashSet();

        var peopleIds = followUpIds.Concat(classes.Where(c => c.HomeroomTeacherUserId != null).Select(c => c.HomeroomTeacherUserId!.Value)).Distinct().ToList();
        var names = await _platform.Users.AsNoTracking().Where(u => peopleIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FullName == "" ? u.UserName! : u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        var classById = classes.ToDictionary(c => c.Id);
        var followUp = followUpIds
            .Select(id =>
            {
                var cls = roster.Where(r => r.UserId == id).Select(r => classById[r.ClassGroupId]).MinBy(c => c.Name);
                return new OverviewFollowUpDto(id, names.GetValueOrDefault(id, "—"), cls?.Id, cls?.Name,
                    absences.GetValueOrDefault(id), lates.GetValueOrDefault(id), averages.TryGetValue(id, out var a) ? a : null);
            })
            .OrderByDescending(f => f.Absent).ThenByDescending(f => f.Late).ThenBy(f => f.AveragePercent ?? 100).ThenBy(f => f.FullName)
            .ToList();

        // Classes ---------------------------------------------------------------------------------------------
        var classRows = classes.Select(c =>
        {
            var students = roster.Where(r => r.ClassGroupId == c.Id).Select(r => r.UserId).ToHashSet();
            var courses = courseOfClass.Where(x => x.ClassGroupId == c.Id).Select(x => x.CourseId).ToHashSet();
            int Count(AttendanceStatus status) => byClass.Where(x => x.ClassGroupId == c.Id && x.Status == status).Sum(x => x.Count);
            var classMarks = gradeMarks.Where(g => students.Contains(g.UserId) && courses.Contains(g.CourseId)).ToList();
            var taken = todaySessions.Contains(c.Id);
            var firstStart = c.Kind == ClassKind.Group
                ? c.Schedule.Where(s => s.Day == today.DayOfWeek).Select(s => (TimeOnly?)s.StartTime).Min()
                : null;
            var status = c.Kind == ClassKind.Group
                ? OverviewRules.Group(inYear ? firstStart : null, taken, time, org.GroupAttendanceGraceMinutes)
                : OverviewRules.Section(schoolDay, taken, time, org.AttendanceDueBy);

            return new OverviewClassDto(c.Id, c.Name, c.Kind.ToString(), students.Count,
                c.HomeroomTeacherUserId is { } teacher ? names.GetValueOrDefault(teacher) : null,
                status, firstStart,
                OverviewRules.Rate(Count(AttendanceStatus.Present), Count(AttendanceStatus.Late), Count(AttendanceStatus.Absent)),
                classMarks.Count == 0 ? null : (int)Math.Round(classMarks.Average(m => m.Percent)),
                students.Count(followUpIds.Contains));
        }).ToList();

        // Homework waiting for a grade, oldest first.
        var submissions = _tenant.AssignmentSubmissions.AsNoTracking().Where(s => s.Status == SubmissionStatus.Submitted);
        if (mine)
        {
            var myCourses = _tenant.TeachingAssignments.Where(a => a.TeacherUserId == me).Select(a => a.CourseId)
                .Union(_tenant.Courses.Where(c => c.CreatedByUserId == me).Select(c => c.Id));
            submissions = submissions.Where(s => myCourses.Contains(s.CourseId));
        }
        var waiting = await submissions
            .GroupBy(s => s.CourseId).Select(g => new { CourseId = g.Key, Count = g.Count(), Oldest = g.Min(s => s.SubmittedAtUtc) })
            .ToListAsync(cancellationToken);
        var waitingIds = waiting.Select(w => w.CourseId).ToList();
        var titles = await _tenant.Courses.AsNoTracking().Where(c => waitingIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, cancellationToken);
        var grading = waiting.OrderBy(w => w.Oldest)
            .Select(w => new OverviewGradingDto(w.CourseId, titles.GetValueOrDefault(w.CourseId, "—"), w.Count, w.Oldest)).ToList();

        // People counts ---------------------------------------------------------------------------------------
        var members = _platform.Memberships.AsNoTracking().Where(m => m.OrganizationId == orgId && m.Status == MembershipStatus.Active);
        var studentCount = mine ? roster.Select(r => r.UserId).Distinct().Count()
            : await members.CountAsync(m => (m.Roles & OrganizationRole.Student) != 0, cancellationToken);
        var teacherCount = await members.CountAsync(m => (m.Roles & OrganizationRole.Teacher) != 0, cancellationToken);

        var todayRate = OverviewRules.Rate(TodayCount(AttendanceStatus.Present), TodayCount(AttendanceStatus.Late), TodayCount(AttendanceStatus.Absent));
        var todayDto = new OverviewTodayDto(
            today,
            schoolDay || classRows.Any(c => c.Today != TodayStatus.NotToday),
            classRows.Count(c => c.Today != TodayStatus.NotToday),
            classRows.Count(c => c.Today == TodayStatus.Taken),
            TodayCount(AttendanceStatus.Present), TodayCount(AttendanceStatus.Absent), TodayCount(AttendanceStatus.Late), TodayCount(AttendanceStatus.Excused),
            todayRate,
            earlier.Count == 0 ? null : (int)Math.Round(earlier.Average(d => d.RatePercent)),
            todayRate is null ? earlier.LastOrDefault() : null);

        return Result<OrganizationOverviewDto>.Success(new OrganizationOverviewDto(
            new OverviewSchoolDto(org.Name, org.LogoUrl, org.Type.ToString(), studentCount, teacherCount, classes.Count,
                new OverviewRulesDto(org.FollowUpAbsentDays, org.FollowUpLateDays, org.GoodGradePercent, org.LowGradePercent,
                    org.AttendanceDueBy, org.GroupAttendanceGraceMinutes)),
            todayDto,
            days,
            classRows,
            followUp.Count,
            followUp.Take(FollowUpShown).ToList(),
            grading));
    }

    private static DateTime LocalNow(string timeZone)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found) ? found : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
    }
}
