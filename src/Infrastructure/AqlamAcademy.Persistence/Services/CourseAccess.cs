using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class CourseAccess(
    ApplicationDbContext platform,
    TenantDbContext tenant,
    ICurrentUserService currentUser,
    ICurrentOrganization currentOrganization,
    IMembershipService memberships) : ICourseAccess
{
    private (OrganizationType Type, bool TeachersCreate, OrganizationRole Roles)? _context;

    public async Task<bool> SeesAllCoursesAsync(CancellationToken cancellationToken)
    {
        var (type, _, roles) = await ContextAsync(cancellationToken);
        return type == OrganizationType.Personal || Permissions.Grants(roles, Permissions.ClassesManage);
    }

    public async Task<bool> CanCreateAsync(CancellationToken cancellationToken)
    {
        var (type, teachersCreate, roles) = await ContextAsync(cancellationToken);
        return type switch
        {
            OrganizationType.Personal => Permissions.Grants(roles, Permissions.CoursesEdit),
            OrganizationType.Center => Permissions.Grants(roles, Permissions.ClassesManage)
                || (teachersCreate && Permissions.Grants(roles, Permissions.CoursesEdit)),
            _ => Permissions.Grants(roles, Permissions.ClassesManage),
        };
    }

    public async Task<bool> CanWorkOnAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (await SeesAllCoursesAsync(cancellationToken))
            return true;
        var me = currentUser.UserId;
        return await tenant.Courses.AnyAsync(c => c.Id == courseId && c.CreatedByUserId == me, cancellationToken)
            || await tenant.TeachingAssignments.AnyAsync(a => a.CourseId == courseId && a.TeacherUserId == me, cancellationToken);
    }

    private async Task<(OrganizationType Type, bool TeachersCreate, OrganizationRole Roles)> ContextAsync(CancellationToken cancellationToken)
    {
        if (_context is { } cached)
            return cached;
        var orgId = currentOrganization.OrganizationId ?? throw new InvalidOperationException("No organization resolved.");
        var org = await platform.Organizations.AsNoTracking().Where(o => o.Id == orgId)
            .Select(o => new { o.Type, o.TeachersCreateCourses }).FirstAsync(cancellationToken);
        var roles = await memberships.GetActiveRolesAsync(currentUser.UserId, orgId, cancellationToken);
        _context = (org.Type, org.TeachersCreateCourses, roles);
        return _context.Value;
    }
}
