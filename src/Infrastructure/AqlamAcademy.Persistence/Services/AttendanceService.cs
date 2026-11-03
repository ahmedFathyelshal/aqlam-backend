using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Attendance;
using AqlamAcademy.Application.Features.Communication;
using AqlamAcademy.Domain.Attendance;
using AqlamAcademy.Domain.Communication;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Attendance sheets per class and day (or meeting). Saving is an idempotent upsert so a phone that was offline
/// can resend; parents are notified only when a child's status changes to absent or late.
/// </summary>
public class AttendanceService : IAttendanceService
{
    private readonly TenantDbContext _tenant;
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentOrganization _currentOrganization;
    private readonly INotifier _notifier;

    public AttendanceService(TenantDbContext tenant, ApplicationDbContext platform, ICurrentUserService currentUser,
        ICurrentOrganization currentOrganization, INotifier notifier)
    {
        _tenant = tenant;
        _platform = platform;
        _currentUser = currentUser;
        _currentOrganization = currentOrganization;
        _notifier = notifier;
    }

    public async Task<Result<AttendanceSheetDto>> GetSheetAsync(Guid classId, DateOnly date, TimeOnly? startTime, CancellationToken cancellationToken)
    {
        var classGroup = await _tenant.ClassGroups.AsNoTracking().Include(c => c.Students).FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);
        if (classGroup is null)
            return Result<AttendanceSheetDto>.NotFound("Class not found.");

        var session = await _tenant.AttendanceSessions.AsNoTracking().Include(s => s.Records)
            .FirstOrDefaultAsync(s => s.ClassGroupId == classId && s.Date == date && s.StartTime == startTime, cancellationToken);

        return Result<AttendanceSheetDto>.Success(await ToSheetAsync(classGroup.Id, classGroup.Name, classGroup.Students.Select(s => s.UserId), date, startTime, session, cancellationToken));
    }

    public async Task<Result<AttendanceSheetDto>> SaveSheetAsync(Guid classId, DateOnly date, TimeOnly? startTime, IReadOnlyList<AttendanceMark> marks, CancellationToken cancellationToken)
    {
        if (_currentOrganization.OrganizationId is not { } orgId)
            return Result<AttendanceSheetDto>.NotFound("Organization not found.");

        var classGroup = await _tenant.ClassGroups.AsNoTracking().Include(c => c.Students).FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);
        if (classGroup is null)
            return Result<AttendanceSheetDto>.NotFound("Class not found.");

        if (date > await TodayInOrganizationAsync(orgId, cancellationToken))
            return Result<AttendanceSheetDto>.Failure(["Attendance cannot be taken for a future date."]);

        var session = await _tenant.AttendanceSessions.Include(s => s.Records)
            .FirstOrDefaultAsync(s => s.ClassGroupId == classId && s.Date == date && s.StartTime == startTime, cancellationToken);

        // Students on the roster, plus anyone already on this sheet (they may have left the class since).
        var allowed = classGroup.Students.Select(s => s.UserId).ToHashSet();
        if (session is not null)
            allowed.UnionWith(session.Records.Select(r => r.UserId));
        if (marks.Any(m => !allowed.Contains(m.UserId)))
            return Result<AttendanceSheetDto>.Failure(["Some students are not in this class."]);

        var now = DateTime.UtcNow;
        if (session is null)
        {
            session = new AttendanceSession { ClassGroupId = classId, Date = date, StartTime = startTime, TakenByUserId = _currentUser.UserId, TakenAtUtc = now };
            _tenant.AttendanceSessions.Add(session);
        }
        else
        {
            session.LastModifiedAtUtc = now;
        }

        var alerts = new List<(Guid UserId, AttendanceStatus Status)>();
        foreach (var mark in marks)
        {
            var record = session.Records.FirstOrDefault(r => r.UserId == mark.UserId);
            var previous = record?.Status;
            if (record is null)
            {
                record = new AttendanceRecord { SessionId = session.Id, UserId = mark.UserId };
                _tenant.AttendanceRecords.Add(record); // added explicitly (preset key); EF fix-up puts it in session.Records
            }

            record.Status = mark.Status;
            record.Note = string.IsNullOrWhiteSpace(mark.Note) ? null : mark.Note.Trim();
            if (AttendanceRecord.IsAlert(mark.Status) && previous != mark.Status)
                alerts.Add((mark.UserId, mark.Status));
        }

        await _tenant.SaveChangesAsync(cancellationToken);
        await NotifyParentsAsync(orgId, classGroup.Name, date, alerts, cancellationToken);

        return Result<AttendanceSheetDto>.Success(await ToSheetAsync(classGroup.Id, classGroup.Name, classGroup.Students.Select(s => s.UserId), date, startTime, session, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<AttendanceSummaryRowDto>>> SummaryAsync(Guid classId, DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        var classGroup = await _tenant.ClassGroups.AsNoTracking().Include(c => c.Students).FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);
        if (classGroup is null)
            return Result<IReadOnlyList<AttendanceSummaryRowDto>>.NotFound("Class not found.");

        var counts = await (
                from r in _tenant.AttendanceRecords
                join s in _tenant.AttendanceSessions on r.SessionId equals s.Id
                where s.ClassGroupId == classId && s.Date >= first && s.Date <= last
                group r by new { r.UserId, r.Status } into g
                select new { g.Key.UserId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var userIds = classGroup.Students.Select(s => s.UserId).Union(counts.Select(c => c.UserId)).ToList();
        var people = await PeopleAsync(userIds, cancellationToken);

        int Count(Guid userId, AttendanceStatus status) => counts.FirstOrDefault(c => c.UserId == userId && c.Status == status)?.Count ?? 0;

        return Result<IReadOnlyList<AttendanceSummaryRowDto>>.Success(userIds
            .Where(people.ContainsKey)
            .Select(id => new AttendanceSummaryRowDto(id, people[id].UserName, people[id].FullName,
                Count(id, AttendanceStatus.Present), Count(id, AttendanceStatus.Absent), Count(id, AttendanceStatus.Late), Count(id, AttendanceStatus.Excused)))
            .OrderByDescending(r => r.Absent).ThenBy(r => r.FullName)
            .ToList());
    }

    public async Task<IReadOnlyList<AttendanceEntryDto>> HistoryOfAsync(Guid studentUserId, DateOnly first, DateOnly last, CancellationToken cancellationToken) =>
        await (
                from r in _tenant.AttendanceRecords.AsNoTracking()
                join s in _tenant.AttendanceSessions.AsNoTracking() on r.SessionId equals s.Id
                join c in _tenant.ClassGroups.AsNoTracking() on s.ClassGroupId equals c.Id
                where r.UserId == studentUserId && s.Date >= first && s.Date <= last
                orderby s.Date descending, s.StartTime descending
                select new AttendanceEntryDto(s.Date, s.StartTime, c.Id, c.Name, r.Status.ToString(), r.Note))
            .ToListAsync(cancellationToken);

    private async Task NotifyParentsAsync(Guid orgId, string className, DateOnly date, List<(Guid UserId, AttendanceStatus Status)> alerts, CancellationToken cancellationToken)
    {
        if (alerts.Count == 0)
            return;

        var childIds = alerts.Select(a => a.UserId).ToList();
        var parents = await _platform.ParentLinks.AsNoTracking().Where(l => childIds.Contains(l.ChildUserId)).ToListAsync(cancellationToken);
        if (parents.Count == 0)
            return;

        var org = await _platform.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.Slug).FirstAsync(cancellationToken);
        var names = await PeopleAsync(childIds, cancellationToken);
        foreach (var (childId, status) in alerts)
        {
            var parentIds = parents.Where(p => p.ChildUserId == childId).Select(p => p.ParentUserId).ToList();
            if (parentIds.Count == 0 || !names.TryGetValue(childId, out var child))
                continue;

            var (arabic, english) = status == AttendanceStatus.Absent ? ("غياب", "absent") : ("تأخر", "late");
            await _notifier.NotifyAsync(new OutgoingNotification(
                parentIds,
                orgId,
                status == AttendanceStatus.Absent ? NotificationType.Absence : NotificationType.Late,
                $"{arabic}: {child.FullName}",
                $"تم تسجيل {arabic} {child.FullName} في {className} بتاريخ {date:yyyy-MM-dd}.\n{child.FullName} was marked {english} in {className} on {date:yyyy-MM-dd}.",
                $"/parent/{org}/children/{childId}",
                SendEmail: true), cancellationToken);
        }
    }

    private async Task<DateOnly> TodayInOrganizationAsync(Guid orgId, CancellationToken cancellationToken)
    {
        var timeZoneId = await _platform.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.TimeZone).FirstAsync(cancellationToken);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
    }

    private async Task<AttendanceSheetDto> ToSheetAsync(Guid classId, string className, IEnumerable<Guid> roster, DateOnly date, TimeOnly? startTime,
        AttendanceSession? session, CancellationToken cancellationToken)
    {
        var records = session?.Records.ToDictionary(r => r.UserId) ?? [];
        var userIds = roster.Union(records.Keys).ToList();
        var people = await PeopleAsync(userIds.Append(session?.TakenByUserId ?? Guid.Empty), cancellationToken);

        var lines = userIds
            .Where(people.ContainsKey)
            .Select(id => new AttendanceLineDto(id, people[id].UserName, people[id].FullName,
                records.TryGetValue(id, out var r) ? r.Status.ToString() : null,
                r?.Note))
            .OrderBy(l => l.FullName)
            .ToList();

        return new AttendanceSheetDto(classId, className, date, startTime, session is not null,
            session?.LastModifiedAtUtc ?? session?.TakenAtUtc,
            session is not null && people.TryGetValue(session.TakenByUserId, out var takenBy) ? takenBy.FullName : null,
            lines);
    }

    private sealed record Person(string UserName, string FullName);

    private async Task<Dictionary<Guid, Person>> PeopleAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return await _platform.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new Person(u.UserName!, u.DisplayName), cancellationToken);
    }
}
