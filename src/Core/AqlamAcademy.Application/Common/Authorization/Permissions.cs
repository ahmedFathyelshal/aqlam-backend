using AqlamAcademy.Domain.Organizations;

namespace AqlamAcademy.Application.Common.Authorization;

/// <summary>
/// Permissions checked inside an organization. Endpoints require permissions, never roles,
/// so roles can be reshaped without touching endpoints.
/// </summary>
public static class Permissions
{
    public const string CoursesView = "courses.view";
    public const string CoursesEdit = "courses.edit";
    public const string CoursesPublish = "courses.publish";
    public const string MembersView = "members.view";
    public const string MembersManage = "members.manage";
    public const string AttendanceTake = "attendance.take";
    public const string GradesEdit = "grades.edit";
    public const string FinanceView = "finance.view";
    public const string OrganizationSettings = "org.settings";

    private static readonly Dictionary<OrganizationRole, string[]> RoleGrants = new()
    {
        [OrganizationRole.Owner] = [CoursesView, CoursesEdit, CoursesPublish, MembersView, MembersManage, AttendanceTake, GradesEdit, FinanceView, OrganizationSettings],
        [OrganizationRole.Admin] = [CoursesView, CoursesEdit, CoursesPublish, MembersView, MembersManage, AttendanceTake, GradesEdit, OrganizationSettings],
        [OrganizationRole.AcademicCoordinator] = [CoursesView, CoursesEdit, CoursesPublish, MembersView, AttendanceTake, GradesEdit],
        [OrganizationRole.Teacher] = [CoursesView, CoursesEdit, MembersView, AttendanceTake, GradesEdit],
        [OrganizationRole.Assistant] = [CoursesView, MembersView, AttendanceTake],
        [OrganizationRole.Finance] = [FinanceView, MembersView],
        [OrganizationRole.Student] = [],
        [OrganizationRole.Parent] = []
    };

    public static bool Grants(OrganizationRole roles, string permission) =>
        RoleGrants.Any(grant => roles.HasFlag(grant.Key) && grant.Value.Contains(permission));
}
