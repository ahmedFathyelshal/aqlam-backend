using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MembersAndJoinCodeTests(ApiFactory factory)
{
    [Fact]
    public async Task Center_teachers_create_courses_only_when_the_center_allows_it()
    {
        var (owner, org) = await CreateOrganizationWithOwnerAsync();
        var teacher = factory.CreateBrowserClient();
        var teacherName = await RegisterAsync(teacher, TestUsers.Teacher, "tch");

        var add = await owner.PostAsJsonAsync($"/api/orgs/{org}/members", new { userName = teacherName, roles = new[] { "Teacher" } });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        // Off by default: the center creates its courses.
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/orgs/{org}/courses/draft", new { title = "Grade 8 Math" })).StatusCode);
        var ownerDraft = await owner.PostAsJsonAsync($"/api/orgs/{org}/courses/draft", new { title = "Center course" });
        var ownerCourseId = (await ownerDraft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetString();

        (await owner.PutAsJsonAsync($"/api/orgs/{org}/settings", new { name = "Al Noor Center", timeZone = "Asia/Riyadh", calendar = "Both", weekStartDay = "Sunday", teachersCreateCourses = true }))
            .EnsureSuccessStatusCode();
        var membership = (await teacher.GetFromJsonAsync<JsonElement>("/api/me/memberships")).GetProperty("data").EnumerateArray().Single(m => m.GetProperty("slug").GetString() == org);
        Assert.True(membership.GetProperty("teachersCreateCourses").GetBoolean());

        var draft = await teacher.PostAsJsonAsync($"/api/orgs/{org}/courses/draft", new { title = "Grade 8 Math" });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);

        // A teacher sees and edits their own courses, not the center's other ones.
        var mine = (await teacher.GetFromJsonAsync<JsonElement>($"/api/orgs/{org}/courses")).GetProperty("data");
        Assert.Equal("Grade 8 Math", mine.EnumerateArray().Single().GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/orgs/{org}/courses/{ownerCourseId}/draft")).StatusCode);
        Assert.Equal(2, (await owner.GetFromJsonAsync<JsonElement>($"/api/orgs/{org}/courses")).GetProperty("data").GetArrayLength());

        var members = (await owner.GetFromJsonAsync<JsonElement>($"/api/orgs/{org}/members")).GetProperty("data");
        Assert.Contains(members.EnumerateArray(), m => m.GetProperty("userName").GetString() == teacherName);
    }

    [Fact]
    public async Task Assistant_can_view_members_but_cannot_manage_them()
    {
        var (owner, org) = await CreateOrganizationWithOwnerAsync();
        var assistant = factory.CreateBrowserClient();
        var assistantName = await RegisterAsync(assistant, TestUsers.Teacher, "ast");
        (await owner.PostAsJsonAsync($"/api/orgs/{org}/members", new { userName = assistantName, roles = new[] { "Assistant" } })).EnsureSuccessStatusCode();

        var view = await assistant.GetAsync($"/api/orgs/{org}/members");
        var manage = await assistant.PostAsJsonAsync($"/api/orgs/{org}/join-codes", new { });

        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, manage.StatusCode);
    }

    [Fact]
    public async Task Owner_role_cannot_be_granted_and_owner_cannot_be_removed()
    {
        var (owner, org) = await CreateOrganizationWithOwnerAsync();
        var other = factory.CreateBrowserClient();
        var otherName = await RegisterAsync(other, TestUsers.Teacher, "own2");

        var grantOwner = await owner.PostAsJsonAsync($"/api/orgs/{org}/members", new { userName = otherName, roles = new[] { "Owner" } });
        Assert.Equal(HttpStatusCode.BadRequest, grantOwner.StatusCode);

        var ownerId = (await owner.GetFromJsonAsync<JsonElement>("/api/Auth/me")).GetProperty("data").GetProperty("id").GetString();
        var removeOwner = await owner.DeleteAsync($"/api/orgs/{org}/members/{ownerId}");
        Assert.Equal(HttpStatusCode.Conflict, removeOwner.StatusCode);
    }

    [Fact]
    public async Task Student_joins_with_code_pending_approval_and_admin_approves()
    {
        var (owner, org) = await CreateOrganizationWithOwnerAsync();
        var code = (await (await owner.PostAsJsonAsync($"/api/orgs/{org}/join-codes", new { requiresApproval = true }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("code").GetString()!;
        var student = factory.CreateBrowserClient();
        await RegisterAsync(student, TestUsers.Student, "stj");

        var join = await student.PostAsJsonAsync("/api/me/join", new { code = code.ToLowerInvariant() }); // codes are case-insensitive
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        Assert.Equal("Pending", (await join.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("membershipStatus").GetString());

        var studentId = (await student.GetFromJsonAsync<JsonElement>("/api/Auth/me")).GetProperty("data").GetProperty("id").GetString();
        var approve = await owner.PostAsync($"/api/orgs/{org}/members/{studentId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal("Active", (await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("status").GetString());

        var again = await student.PostAsJsonAsync("/api/me/join", new { code });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Invalid_exhausted_or_deactivated_codes_are_rejected()
    {
        var (owner, org) = await CreateOrganizationWithOwnerAsync();
        var created = (await (await owner.PostAsJsonAsync($"/api/orgs/{org}/join-codes", new { maxUses = 1 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var code = created.GetProperty("code").GetString()!;

        var first = factory.CreateBrowserClient();
        var second = factory.CreateBrowserClient();
        await RegisterAsync(first, TestUsers.Student, "sj1");
        await RegisterAsync(second, TestUsers.Student, "sj2");

        Assert.Equal(HttpStatusCode.NotFound, (await first.PostAsJsonAsync("/api/me/join", new { code = "NOTACODE" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/me/join", new { code })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostAsJsonAsync("/api/me/join", new { code })).StatusCode); // max uses reached

        var another = (await (await owner.PostAsJsonAsync($"/api/orgs/{org}/join-codes", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (await owner.DeleteAsync($"/api/orgs/{org}/join-codes/{another.GetProperty("id").GetString()}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostAsJsonAsync("/api/me/join", new { code = another.GetProperty("code").GetString() })).StatusCode);
    }

    [Fact]
    public async Task Join_codes_cannot_grant_staff_roles()
    {
        var (owner, org) = await CreateOrganizationWithOwnerAsync();

        var response = await owner.PostAsJsonAsync($"/api/orgs/{org}/join-codes", new { role = "Teacher" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A center created by the platform admin, owned by a freshly registered user.</summary>
    private async Task<(HttpClient Owner, string Slug)> CreateOrganizationWithOwnerAsync()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();

        var owner = factory.CreateBrowserClient();
        var ownerName = await RegisterAsync(owner, TestUsers.Teacher, "cown");
        var slug = $"center-{Guid.NewGuid():N}"[..20];

        (await admin.PostAsJsonAsync("/api/platform/organizations", new
        {
            name = "Al Noor Center",
            slug,
            type = "Center",
            countryCode = "SA",
            ownerUserName = ownerName
        })).EnsureSuccessStatusCode();

        return (owner, slug);
    }

    private static async Task<string> RegisterAsync(HttpClient client, int userType, string prefix) =>
        (await TestUsers.RegisterAndLoginAsync(client, userType, prefix)).GetProperty("data").GetProperty("userName").GetString()!;
}
