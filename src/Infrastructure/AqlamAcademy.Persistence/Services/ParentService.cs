using System.Security.Cryptography;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Attendance;
using AqlamAcademy.Application.Features.Parents;
using AqlamAcademy.Domain.Assessments;
using AqlamAcademy.Domain.Attendance;
using AqlamAcademy.Domain.Learning;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Parent ↔ child links and the parents' read-only view. A link is created by organization staff (directly or
/// through a one-time invite code) and gives the parent a Parent membership in that organization.
/// </summary>
public class ParentService : IParentService
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);
    private const int AttendanceDaysShown = 20;
    private const int AttendanceDaysLookBack = 60;

    private readonly ApplicationDbContext _platform;
    private readonly TenantDbContext _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentOrganization _currentOrganization;
    private readonly IMembershipService _memberships;
    private readonly IAttendanceService _attendance;
    private readonly IIdentityService _identity;

    public ParentService(ApplicationDbContext platform, TenantDbContext tenant, ICurrentUserService currentUser,
        ICurrentOrganization currentOrganization, IMembershipService memberships, IAttendanceService attendance, IIdentityService identity)
    {
        _identity = identity;
        _platform = platform;
        _tenant = tenant;
        _currentUser = currentUser;
        _currentOrganization = currentOrganization;
        _memberships = memberships;
        _attendance = attendance;
    }

    private Guid OrgId => _currentOrganization.OrganizationId ?? throw new InvalidOperationException("No organization resolved.");

    // ---- Organization staff ---------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<ParentDto>>> ListParentsAsync(Guid childUserId, CancellationToken cancellationToken)
    {
        if (!await IsStudentAsync(childUserId, OrgId, cancellationToken))
            return Result<IReadOnlyList<ParentDto>>.NotFound("Student not found.");

        var parents = await (
                from l in _platform.ParentLinks.AsNoTracking()
                join u in _platform.Users.AsNoTracking() on l.ParentUserId equals u.Id
                where l.ChildUserId == childUserId
                orderby l.CreatedAtUtc
                select new ParentDto(u.Id, u.UserName!, u.FullName == "" ? u.UserName! : u.FullName, u.PhoneNumber, l.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<ParentDto>>.Success(parents);
    }

    public async Task<Result<ParentDto>> LinkParentAsync(Guid childUserId, string parentUserName, CancellationToken cancellationToken)
    {
        var orgId = OrgId;
        if (!await IsStudentAsync(childUserId, orgId, cancellationToken))
            return Result<ParentDto>.NotFound("Student not found.");

        var parent = await _platform.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == parentUserName.ToUpperInvariant(), cancellationToken);
        if (parent is null)
            return Result<ParentDto>.NotFound($"User '{parentUserName}' was not found.");

        if (await CheckCanLinkAsync(parent.Id, childUserId, cancellationToken) is { } failure)
            return Result<ParentDto>.Failure(failure.ErrorType, failure.Errors);

        var link = await CreateLinkAsync(parent.Id, childUserId, orgId, cancellationToken);
        return Result<ParentDto>.Success(new ParentDto(parent.Id, parent.UserName!, parent.DisplayName, parent.PhoneNumber, link.CreatedAtUtc));
    }

    public async Task<Result<bool>> UnlinkParentAsync(Guid childUserId, Guid parentUserId, CancellationToken cancellationToken)
    {
        var orgId = OrgId;
        if (!await IsStudentAsync(childUserId, orgId, cancellationToken))
            return Result<bool>.NotFound("Student not found.");

        var link = await _platform.ParentLinks.FirstOrDefaultAsync(l => l.ChildUserId == childUserId && l.ParentUserId == parentUserId, cancellationToken);
        if (link is null)
            return Result<bool>.NotFound("This parent is not linked to the student.");

        _platform.ParentLinks.Remove(link);

        // Drop the Parent role when no other child of theirs studies here.
        var otherChildren = await _platform.ParentLinks.Where(l => l.ParentUserId == parentUserId && l.ChildUserId != childUserId)
            .Select(l => l.ChildUserId).ToListAsync(cancellationToken);
        var stillParentHere = await _platform.Memberships.AnyAsync(m => m.OrganizationId == orgId && otherChildren.Contains(m.UserId)
            && m.Status == MembershipStatus.Active && (m.Roles & OrganizationRole.Student) != 0, cancellationToken);
        if (!stillParentHere)
        {
            var membership = await _platform.Memberships.FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == parentUserId, cancellationToken);
            if (membership is not null)
            {
                membership.Roles &= ~OrganizationRole.Parent;
                if (membership.Roles == OrganizationRole.None)
                    membership.Status = MembershipStatus.Left;
            }
        }

        await _platform.SaveChangesAsync(cancellationToken);
        _memberships.Invalidate(parentUserId, orgId);
        return Result<bool>.Success(true);
    }

    public async Task<Result<ParentInviteDto>> CreateInviteAsync(Guid childUserId, CancellationToken cancellationToken)
    {
        var orgId = OrgId;
        if (!await IsStudentAsync(childUserId, orgId, cancellationToken))
            return Result<ParentInviteDto>.NotFound("Student not found.");
        if (await _platform.ParentLinks.CountAsync(l => l.ChildUserId == childUserId, cancellationToken) >= ParentLink.MaxParentsPerChild)
            return Result<ParentInviteDto>.Conflict($"This student already has {ParentLink.MaxParentsPerChild} parents linked.");

        var invite = new ParentInvite { OrganizationId = orgId, ChildUserId = childUserId, ExpiresAtUtc = DateTime.UtcNow.Add(InviteLifetime) };
        do invite.Code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
        while (await _platform.ParentInvites.AnyAsync(i => i.Code == invite.Code, cancellationToken));

        _platform.ParentInvites.Add(invite);
        await _platform.SaveChangesAsync(cancellationToken);

        var childName = await _platform.Users.Where(u => u.Id == childUserId).Select(u => u.FullName == "" ? u.UserName! : u.FullName).FirstAsync(cancellationToken);
        return Result<ParentInviteDto>.Success(new ParentInviteDto(invite.Code, childName, invite.ExpiresAtUtc));
    }

    // ---- Parents --------------------------------------------------------------------------------------------

    public async Task<Result<ChildDto>> RedeemInviteAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().Replace("-", string.Empty).ToUpperInvariant();
        var invite = await _platform.ParentInvites.FirstOrDefaultAsync(i => i.Code == normalized, cancellationToken);
        if (invite is null || invite.UsedAtUtc is not null || invite.ExpiresAtUtc < DateTime.UtcNow)
            return Result<ChildDto>.NotFound("This code is not valid or has expired. Ask the school for a new one.");
        if (invite.OrganizationId is { } inviteOrg && !await IsStudentAsync(invite.ChildUserId, inviteOrg, cancellationToken))
            return Result<ChildDto>.NotFound("This code is not valid or has expired. Ask the school for a new one.");

        var parentId = _currentUser.UserId;
        if (await CheckCanLinkAsync(parentId, invite.ChildUserId, cancellationToken) is { } failure)
            return Result<ChildDto>.Failure(failure.ErrorType, failure.Errors);

        invite.UsedAtUtc = DateTime.UtcNow;
        invite.UsedByUserId = parentId;
        await CreateLinkAsync(parentId, invite.ChildUserId, invite.OrganizationId, cancellationToken);

        return Result<ChildDto>.Success((await MyChildrenAsync(cancellationToken)).First(c => c.UserId == invite.ChildUserId));
    }

    /// <summary>A student's own family code, for a parent who will pay or follow them outside any school.</summary>
    public async Task<Result<ParentInviteDto>> CreateFamilyInviteAsync(CancellationToken cancellationToken)
    {
        var childId = _currentUser.UserId;
        if (await _platform.ParentLinks.CountAsync(l => l.ChildUserId == childId, cancellationToken) >= ParentLink.MaxParentsPerChild)
            return Result<ParentInviteDto>.Conflict($"You already have {ParentLink.MaxParentsPerChild} parents linked.");

        // One open family code at a time: asking again shows the same one.
        var open = await _platform.ParentInvites.FirstOrDefaultAsync(i => i.ChildUserId == childId && i.OrganizationId == null
            && i.UsedAtUtc == null && i.ExpiresAtUtc > DateTime.UtcNow, cancellationToken);
        if (open is null)
        {
            open = new ParentInvite { ChildUserId = childId, ExpiresAtUtc = DateTime.UtcNow.Add(InviteLifetime) };
            do open.Code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
            while (await _platform.ParentInvites.AnyAsync(i => i.Code == open.Code, cancellationToken));
            _platform.ParentInvites.Add(open);
            await _platform.SaveChangesAsync(cancellationToken);
        }

        var name = await _platform.Users.Where(u => u.Id == childId).Select(u => u.FullName == "" ? u.UserName! : u.FullName).FirstAsync(cancellationToken);
        return Result<ParentInviteDto>.Success(new ParentInviteDto(open.Code, name, open.ExpiresAtUtc));
    }

    /// <summary>A parent creates the account of a child (required under 13) and is linked to it at once.</summary>
    public async Task<Result<ChildDto>> CreateChildAccountAsync(CreateChildAccountCommand command, CancellationToken cancellationToken)
    {
        var parentId = _currentUser.UserId;
        var created = await _identity.CreateUserAsync(command.UserName.Trim(), string.Empty, command.Password, Domain.Enums.UserType.Student,
            command.BirthYear, command.FullName?.Trim());
        if (!created.Succeeded)
            return Result<ChildDto>.Failure(created.Errors);

        await CreateLinkAsync(parentId, created.Data, null, cancellationToken);
        return Result<ChildDto>.Success((await MyChildrenAsync(cancellationToken)).First(c => c.UserId == created.Data));
    }

    /// <summary>The child is linked to the signed-in parent.</summary>
    public Task<bool> IsMyChildAsync(Guid childUserId, CancellationToken cancellationToken) =>
        _platform.ParentLinks.AnyAsync(l => l.ParentUserId == _currentUser.UserId && l.ChildUserId == childUserId, cancellationToken);

    public async Task<IReadOnlyList<ChildDto>> MyChildrenAsync(CancellationToken cancellationToken)
    {
        var parentId = _currentUser.UserId;
        var children = await (
                from l in _platform.ParentLinks.AsNoTracking()
                join u in _platform.Users.AsNoTracking() on l.ChildUserId equals u.Id
                where l.ParentUserId == parentId
                orderby u.FullName
                select new { u.Id, u.UserName, u.FullName })
            .ToListAsync(cancellationToken);
        var childIds = children.Select(c => c.Id).ToList();

        // Organizations where the child studies and the parent is an active Parent member.
        var orgs = await (
                from child in _platform.Memberships.AsNoTracking()
                join parent in _platform.Memberships.AsNoTracking() on child.OrganizationId equals parent.OrganizationId
                join o in _platform.Organizations.AsNoTracking() on child.OrganizationId equals o.Id
                where childIds.Contains(child.UserId) && child.Status == MembershipStatus.Active && (child.Roles & OrganizationRole.Student) != 0
                      && parent.UserId == parentId && parent.Status == MembershipStatus.Active && (parent.Roles & OrganizationRole.Parent) != 0
                      && o.Status == OrganizationStatus.Active
                select new { child.UserId, Org = new ChildOrganizationDto(o.Id, o.Slug, o.Name) })
            .ToListAsync(cancellationToken);

        return children.Select(c => new ChildDto(c.Id, c.UserName!, string.IsNullOrWhiteSpace(c.FullName) ? c.UserName! : c.FullName,
            orgs.Where(o => o.UserId == c.Id).Select(o => o.Org).ToList())).ToList();
    }

    public async Task<Result<ChildOverviewDto>> ChildOverviewAsync(Guid childUserId, CancellationToken cancellationToken)
    {
        var orgId = OrgId;
        var linked = await _platform.ParentLinks.AnyAsync(l => l.ParentUserId == _currentUser.UserId && l.ChildUserId == childUserId, cancellationToken);
        if (!linked || !await IsStudentAsync(childUserId, orgId, cancellationToken))
            return Result<ChildOverviewDto>.NotFound("Child not found.");

        var fullName = await _platform.Users.Where(u => u.Id == childUserId).Select(u => u.FullName == "" ? u.UserName! : u.FullName).FirstAsync(cancellationToken);

        var classes = await (
                from s in _tenant.ClassStudents.AsNoTracking()
                join c in _tenant.ClassGroups.AsNoTracking() on s.ClassGroupId equals c.Id
                where s.UserId == childUserId
                orderby c.Name
                select c.Name)
            .ToListAsync(cancellationToken);

        var courses = await _platform.Enrollments.AsNoTracking()
            .Where(e => e.UserId == childUserId && e.OrganizationId == orgId && e.Status == EnrollmentStatus.Active)
            .OrderByDescending(e => e.LastAccessedAtUtc ?? e.StartsAtUtc)
            .Select(e => new ChildCourseDto(e.CourseId, e.CourseTitle, e.ProgressPercent, e.LastAccessedAtUtc))
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // 20 school days can reach further back than the 30 days the totals cover (holidays).
        var longHistory = await _attendance.HistoryOfAsync(childUserId, today.AddDays(-AttendanceDaysLookBack), today.AddDays(1), cancellationToken);
        var history = longHistory.Where(h => h.Date >= today.AddDays(-30)).ToList();
        int Count(AttendanceStatus status) => history.Count(h => h.Status == status.ToString());
        var attendance = new ChildAttendanceDto(Count(AttendanceStatus.Present), Count(AttendanceStatus.Absent), Count(AttendanceStatus.Late),
            Count(AttendanceStatus.Excused), history.Take(10).ToList(), SchoolDays(longHistory));

        var school = await _platform.Organizations.AsNoTracking().Where(o => o.Id == orgId)
            .Select(o => new ChildSchoolDto(o.Name, o.LogoUrl, o.ContactPhone, o.ContactEmail,
                new ChildSummaryRulesDto(o.FollowUpAbsentDays, o.FollowUpLateDays, o.GoodGradePercent, o.LowGradePercent)))
            .FirstAsync(cancellationToken);

        return Result<ChildOverviewDto>.Success(new ChildOverviewDto(childUserId, fullName, classes, courses, attendance, await GradesAsync(childUserId, cancellationToken), school));
    }

    // ---- helpers --------------------------------------------------------------------------------------------

    /// <summary>The latest school days, oldest first, each with its most serious status.</summary>
    private static List<ChildAttendanceDayDto> SchoolDays(IEnumerable<AttendanceEntryDto> history)
    {
        static int Severity(string status) => status switch
        {
            nameof(AttendanceStatus.Absent) => 3,
            nameof(AttendanceStatus.Late) => 2,
            nameof(AttendanceStatus.Excused) => 1,
            _ => 0
        };

        return history.GroupBy(h => h.Date)
            .OrderByDescending(day => day.Key)
            .Take(AttendanceDaysShown)
            .Select(day => new ChildAttendanceDayDto(day.Key, day.MaxBy(h => Severity(h.Status))!.Status))
            .OrderBy(day => day.Date)
            .ToList();
    }

    /// <summary>Latest results: submitted quiz attempts and graded / returned assignments.</summary>
    private async Task<IReadOnlyList<ChildGradeDto>> GradesAsync(Guid childUserId, CancellationToken cancellationToken)
    {
        var quizzes = await _tenant.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == childUserId && a.SubmittedAtUtc != null)
            .OrderByDescending(a => a.SubmittedAtUtc).Take(20)
            .Select(a => new { a.CourseId, a.LessonId, a.Score, a.MaxScore, a.Percent, a.Passed, At = a.SubmittedAtUtc!.Value })
            .ToListAsync(cancellationToken);
        var assignments = await _tenant.AssignmentSubmissions.AsNoTracking()
            .Where(s => s.UserId == childUserId && s.Status != SubmissionStatus.Submitted)
            .OrderByDescending(s => s.GradedAtUtc).Take(20)
            .Select(s => new { s.CourseId, s.LessonId, s.Score, s.Status, At = s.GradedAtUtc ?? s.SubmittedAtUtc })
            .ToListAsync(cancellationToken);

        var lessonIds = quizzes.Select(q => q.LessonId).Concat(assignments.Select(a => a.LessonId)).Distinct().ToList();
        var titles = await _tenant.CourseLessons.AsNoTracking().Where(l => lessonIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Title, cancellationToken);
        var maxPoints = await _tenant.Assignments.AsNoTracking().Where(a => lessonIds.Contains(a.LessonId)).ToDictionaryAsync(a => a.LessonId, a => a.MaxPoints, cancellationToken);

        return quizzes
            .Select(q => new ChildGradeDto("Quiz", q.CourseId, titles.GetValueOrDefault(q.LessonId), q.Score, q.MaxScore, q.Percent, q.Passed ? "Passed" : "NotPassed", q.At))
            .Concat(assignments.Select(a =>
            {
                var max = maxPoints.GetValueOrDefault(a.LessonId, 10);
                return new ChildGradeDto("Assignment", a.CourseId, titles.GetValueOrDefault(a.LessonId), a.Score, max,
                    a.Score is { } score && max > 0 ? Math.Round(score * 100 / max, 1) : null, a.Status.ToString(), a.At);
            }))
            .OrderByDescending(g => g.AtUtc)
            .Take(20)
            .ToList();
    }

    private Task<bool> IsStudentAsync(Guid userId, Guid orgId, CancellationToken cancellationToken) =>
        _platform.Memberships.AnyAsync(m => m.OrganizationId == orgId && m.UserId == userId
            && m.Status == MembershipStatus.Active && (m.Roles & OrganizationRole.Student) != 0, cancellationToken);

    private async Task<Result?> CheckCanLinkAsync(Guid parentId, Guid childId, CancellationToken cancellationToken)
    {
        if (parentId == childId)
            return Result<bool>.Failure(["A student cannot be their own parent."]);
        var links = await _platform.ParentLinks.Where(l => l.ChildUserId == childId).Select(l => l.ParentUserId).ToListAsync(cancellationToken);
        if (links.Contains(parentId))
            return Result<bool>.Conflict("This parent is already linked to the student.");
        if (links.Count >= ParentLink.MaxParentsPerChild)
            return Result<bool>.Conflict($"This student already has {ParentLink.MaxParentsPerChild} parents linked.");
        return null;
    }

    /// <summary>
    /// Creates the link and, when an organization verified it, makes sure the parent is an active Parent member there.
    /// A family link without an organization gives no school access: the school links parents itself.
    /// </summary>
    private async Task<ParentLink> CreateLinkAsync(Guid parentId, Guid childId, Guid? organizationId, CancellationToken cancellationToken)
    {
        var link = new ParentLink { ParentUserId = parentId, ChildUserId = childId, OrganizationId = organizationId };
        _platform.ParentLinks.Add(link);
        if (organizationId is not { } orgId)
        {
            await _platform.SaveChangesAsync(cancellationToken);
            return link;
        }

        var membership = await _platform.Memberships.FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == parentId, cancellationToken);
        if (membership is null)
        {
            _platform.Memberships.Add(new Membership
            {
                OrganizationId = orgId, UserId = parentId, Roles = OrganizationRole.Parent, Source = MembershipSource.ParentLink
            });
        }
        else
        {
            if (membership.Status is MembershipStatus.Left)
            {
                membership.Roles = OrganizationRole.None;
                membership.JoinedAtUtc = DateTime.UtcNow;
            }
            membership.Roles |= OrganizationRole.Parent;
            if (membership.Status is MembershipStatus.Left or MembershipStatus.Pending)
                membership.Status = MembershipStatus.Active;
        }

        await _platform.SaveChangesAsync(cancellationToken);
        _memberships.Invalidate(parentId, orgId);
        return link;
    }
}
