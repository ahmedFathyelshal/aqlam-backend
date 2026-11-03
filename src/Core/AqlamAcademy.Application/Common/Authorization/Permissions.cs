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
    public const string ClassesManage = "classes.manage";
    public const string AttendanceTake = "attendance.take";
    public const string AnnouncementsSend = "announcements.send";
    public const string ChildrenView = "children.view";
    public const string GradesEdit = "grades.edit";
    public const string FinanceView = "finance.view";
    public const string OrganizationSettings = "org.settings";

    private static readonly Dictionary<OrganizationRole, string[]> RoleGrants = new()
    {
        [OrganizationRole.Owner] = [CoursesView, CoursesEdit, CoursesPublish, MembersView, MembersManage, ClassesManage, AttendanceTake, AnnouncementsSend, GradesEdit, FinanceView, OrganizationSettings],
        [OrganizationRole.Admin] = [CoursesView, CoursesEdit, CoursesPublish, MembersView, MembersManage, ClassesManage, AttendanceTake, AnnouncementsSend, GradesEdit, OrganizationSettings],
        [OrganizationRole.AcademicCoordinator] = [CoursesView, CoursesEdit, CoursesPublish, MembersView, ClassesManage, AttendanceTake, AnnouncementsSend, GradesEdit],
        [OrganizationRole.Teacher] = [CoursesView, CoursesEdit, MembersView, AttendanceTake, AnnouncementsSend, GradesEdit],
        [OrganizationRole.Assistant] = [CoursesView, MembersView, AttendanceTake],
        [OrganizationRole.Finance] = [FinanceView, MembersView],
        [OrganizationRole.Student] = [],
        [OrganizationRole.Parent] = [ChildrenView]
    };

    public static bool Grants(OrganizationRole roles, string permission) =>
        RoleGrants.Any(grant => roles.HasFlag(grant.Key) && grant.Value.Contains(permission));
}
