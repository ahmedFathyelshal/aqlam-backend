using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Communication;
using AqlamAcademy.Domain.Communication;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Announcements to the whole organization, a grade or a class/group, delivered as notifications.
/// Class managers may address any scope; teachers only the classes they teach or are homeroom teacher of.
/// </summary>
public class AnnouncementService : IAnnouncementService
{
    private const OrganizationRole StaffRoles = OrganizationRole.Owner | OrganizationRole.Admin | OrganizationRole.AcademicCoordinator
                                                | OrganizationRole.Teacher | OrganizationRole.Assistant | OrganizationRole.Finance;

    private readonly TenantDbContext _tenant;
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentOrganization _currentOrganization;
    private readonly IMembershipService _memberships;
    private readonly INotifier _notifier;

    public AnnouncementService(TenantDbContext tenant, ApplicationDbContext platform, ICurrentUserService currentUser,
        ICurrentOrganization currentOrganization, IMembershipService memberships, INotifier notifier)
    {
        _tenant = tenant;
        _platform = platform;
        _currentUser = currentUser;
        _currentOrganization = currentOrganization;
        _memberships = memberships;
        _notifier = notifier;
    }

    public async Task<Result<AnnouncementDto>> PublishAsync(PublishAnnouncementCommand command, CancellationToken cancellationToken)
    {
        if (_currentOrganization.OrganizationId is not { } orgId)
            return Result<AnnouncementDto>.NotFound("Organization not found.");

        var me = _currentUser.UserId;
        var isManager = Permissions.Grants(await _memberships.GetActiveRolesAsync(me, orgId, cancellationToken), Permissions.ClassesManage);

        List<Guid> classIds;
        switch (command.Scope)
        {
            case AnnouncementScope.Organization:
                if (!isManager)
                    return Result<AnnouncementDto>.Failure(ResultErrorType.Forbidden, "Only school administrators can address the whole organization.");
                classIds = [];
                break;
            case AnnouncementScope.Grade:
                if (!isManager)
                    return Result<AnnouncementDto>.Failure(ResultErrorType.Forbidden, "Only school administrators can address a whole grade.");
                classIds = await _tenant.ClassGroups.Where(c => c.GradeId == command.GradeId).Select(c => c.Id).ToListAsync(cancellationToken);
                if (classIds.Count == 0)
                    return Result<AnnouncementDto>.Failure(["No class is linked to this grade."]);
                break;
            default:
                var classGroup = await _tenant.ClassGroups.AsNoTracking().FirstOrDefaultAsync(c => c.Id == command.ClassGroupId, cancellationToken);
                if (classGroup is null)
                    return Result<AnnouncementDto>.NotFound("Class not found.");
                if (!isManager && classGroup.HomeroomTeacherUserId != me
                    && !await _tenant.TeachingAssignments.AnyAsync(a => a.ClassGroupId == classGroup.Id && a.TeacherUserId == me, cancellationToken))
                    return Result<AnnouncementDto>.Failure(ResultErrorType.Forbidden, "You can only send announcements to classes you teach.");
                classIds = [classGroup.Id];
                break;
        }

        var audience = command.Audience.Aggregate(AnnouncementAudience.None, (all, a) => all | a);
        var recipients = command.Scope == AnnouncementScope.Organization
            ? await OrganizationRecipientsAsync(orgId, audience, cancellationToken)
            : await ClassRecipientsAsync(classIds, audience, cancellationToken);
        recipients.Remove(me);

        var announcement = new Announcement
        {
            Title = command.Title,
            Body = command.Body,
            Scope = command.Scope,
            GradeId = command.Scope == AnnouncementScope.Grade ? command.GradeId : null,
            ClassGroupId = command.Scope == AnnouncementScope.Class ? command.ClassGroupId : null,
            Audience = audience,
            SendEmail = command.SendEmail,
            AuthorUserId = me,
            RecipientCount = recipients.Count
        };
        _tenant.Announcements.Add(announcement);
        await _tenant.SaveChangesAsync(cancellationToken);

        await _notifier.NotifyAsync(new OutgoingNotification(recipients, orgId, NotificationType.Announcement, command.Title, command.Body, null, command.SendEmail), cancellationToken);

        return Result<AnnouncementDto>.Success((await ToDtosAsync([announcement], cancellationToken))[0]);
    }

    public async Task<IReadOnlyList<AnnouncementDto>> ListAsync(Guid? classGroupId, CancellationToken cancellationToken)
    {
        var query = _tenant.Announcements.AsNoTracking();
        if (classGroupId is not null)
            query = query.Where(a => a.ClassGroupId == classGroupId);
        return await ToDtosAsync(await query.OrderByDescending(a => a.PublishedAtUtc).Take(100).ToListAsync(cancellationToken), cancellationToken);
    }

    private async Task<HashSet<Guid>> OrganizationRecipientsAsync(Guid orgId, AnnouncementAudience audience, CancellationToken cancellationToken)
    {
        var wanted = OrganizationRole.None;
        if (audience.HasFlag(AnnouncementAudience.Students)) wanted |= OrganizationRole.Student;
        if (audience.HasFlag(AnnouncementAudience.Parents)) wanted |= OrganizationRole.Parent;
        if (audience.HasFlag(AnnouncementAudience.Staff)) wanted |= StaffRoles;

        return (await _platform.Memberships.AsNoTracking()
                .Where(m => m.OrganizationId == orgId && m.Status == MembershipStatus.Active && (m.Roles & wanted) != 0)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    private async Task<HashSet<Guid>> ClassRecipientsAsync(List<Guid> classIds, AnnouncementAudience audience, CancellationToken cancellationToken)
    {
        var recipients = new HashSet<Guid>();
        var students = await _tenant.ClassStudents.Where(s => classIds.Contains(s.ClassGroupId)).Select(s => s.UserId).Distinct().ToListAsync(cancellationToken);

        if (audience.HasFlag(AnnouncementAudience.Students))
            recipients.UnionWith(students);
        if (audience.HasFlag(AnnouncementAudience.Parents))
            recipients.UnionWith(await _platform.ParentLinks.Where(l => students.Contains(l.ChildUserId)).Select(l => l.ParentUserId).ToListAsync(cancellationToken));
        if (audience.HasFlag(AnnouncementAudience.Staff))
        {
            recipients.UnionWith(await _tenant.TeachingAssignments.Where(a => classIds.Contains(a.ClassGroupId)).Select(a => a.TeacherUserId).ToListAsync(cancellationToken));
            recipients.UnionWith(await _tenant.ClassGroups.Where(c => classIds.Contains(c.Id) && c.HomeroomTeacherUserId != null)
                .Select(c => c.HomeroomTeacherUserId!.Value).ToListAsync(cancellationToken));
        }
        return recipients;
    }

    private async Task<List<AnnouncementDto>> ToDtosAsync(List<Announcement> announcements, CancellationToken cancellationToken)
    {
        var authorIds = announcements.Select(a => a.AuthorUserId).Distinct().ToList();
        var authors = await _platform.Users.AsNoTracking().Where(u => authorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var classIds = announcements.Where(a => a.ClassGroupId != null).Select(a => a.ClassGroupId!.Value).Distinct().ToList();
        var classes = await _tenant.ClassGroups.AsNoTracking().Where(c => classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return announcements.Select(a => new AnnouncementDto(
            a.Id, a.Title, a.Body, a.Scope.ToString(), a.GradeId, a.ClassGroupId,
            a.ClassGroupId is { } c ? classes.GetValueOrDefault(c) : null,
            Enum.GetValues<AnnouncementAudience>().Where(x => x != AnnouncementAudience.None && a.Audience.HasFlag(x)).Select(x => x.ToString()).ToList(),
            a.SendEmail, a.AuthorUserId, authors.GetValueOrDefault(a.AuthorUserId), a.PublishedAtUtc, a.RecipientCount)).ToList();
    }
}
