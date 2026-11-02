using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OrganizationIsolationTests(ApiFactory factory)
{
    [Fact]
    public async Task Teacher_registration_creates_a_personal_organization_owned_by_the_teacher()
    {
        var teacher = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teacher, TestUsers.Teacher, "own");

        var memberships = await GetMembershipsAsync(teacher);

        var membership = Assert.Single(memberships.EnumerateArray());
        Assert.Equal("Personal", membership.GetProperty("type").GetString());
        var roles = membership.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("Owner", roles);
        Assert.Contains("Teacher", roles);
    }

    [Fact]
    public async Task Student_registration_creates_no_organization()
    {
        var student = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, "stu");

        var memberships = await GetMembershipsAsync(student);

        Assert.Empty(memberships.EnumerateArray());
    }

    [Fact]
    public async Task Anonymous_user_gets_401_on_organization_endpoints()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.PostAsJsonAsync("/api/orgs/anything/courses/draft", new { title = "Physics" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_organization_returns_404()
    {
        var teacher = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teacher, TestUsers.Teacher, "unk");

        var response = await teacher.PostAsJsonAsync("/api/orgs/does-not-exist/courses/draft", new { title = "Physics" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Non_member_gets_404_for_another_organizations_courses()
    {
        var teacherA = factory.CreateBrowserClient();
        var outsider = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teacherA, TestUsers.Teacher, "ta");
        await TestUsers.RegisterAndLoginAsync(outsider, TestUsers.Student, "out");
        var orgA = await GetFirstOrgSlugAsync(teacherA);
        var courseId = await CreateDraftAsync(teacherA, orgA);

        var read = await outsider.GetAsync($"/api/orgs/{orgA}/courses/{courseId}/draft");
        var create = await outsider.PostAsJsonAsync($"/api/orgs/{orgA}/courses/draft", new { title = "Intruder" });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_reach_another_organizations_course_through_their_own_organization()
    {
        var teacherA = factory.CreateBrowserClient();
        var teacherB = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teacherA, TestUsers.Teacher, "ta");
        await TestUsers.RegisterAndLoginAsync(teacherB, TestUsers.Teacher, "tb");
        var orgA = await GetFirstOrgSlugAsync(teacherA);
        var orgB = await GetFirstOrgSlugAsync(teacherB);
        var courseOfA = await CreateDraftAsync(teacherA, orgA);

        // B is a legitimate member of orgB, but the course id belongs to orgA: the tenant filter must hide it.
        var readViaOwnOrg = await teacherB.GetAsync($"/api/orgs/{orgB}/courses/{courseOfA}/draft");
        var updateViaOwnOrg = await teacherB.PutAsJsonAsync($"/api/orgs/{orgB}/courses/{courseOfA}/media", new
        {
            id = courseOfA,
            subtitle = "hijacked",
            thumbnailUrl = (string?)null,
            promoVideoUrl = (string?)null
        });

        Assert.Equal(HttpStatusCode.NotFound, readViaOwnOrg.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, updateViaOwnOrg.StatusCode);

        var draft = await (await teacherA.GetAsync($"/api/orgs/{orgA}/courses/{courseOfA}/draft"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual("hijacked", draft.GetProperty("data").GetProperty("subtitle").GetString());
    }

    [Fact]
    public async Task Owner_can_create_and_read_courses_in_their_organization()
    {
        var teacher = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teacher, TestUsers.Teacher, "crud");
        var org = await GetFirstOrgSlugAsync(teacher);

        var courseId = await CreateDraftAsync(teacher, org);
        var read = await teacher.GetAsync($"/api/orgs/{org}/courses/{courseId}/draft");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    private static async Task<JsonElement> GetMembershipsAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<JsonElement>("/api/me/memberships");
        return body.GetProperty("data");
    }

    private static async Task<string> GetFirstOrgSlugAsync(HttpClient client) =>
        (await GetMembershipsAsync(client))[0].GetProperty("slug").GetString()!;

    private static async Task<string> CreateDraftAsync(HttpClient teacher, string org)
    {
        var stage = await FirstLookupIdAsync(teacher, "/api/Lookups/education-stages");
        var grade = await FirstLookupIdAsync(teacher, $"/api/Lookups/education-grades?stageId={stage}");
        var subject = await FirstLookupIdAsync(teacher, "/api/Lookups/subjects");

        var response = await teacher.PostAsJsonAsync($"/api/orgs/{org}/courses/draft", new
        {
            title = "Modern Physics",
            stageId = stage,
            gradeId = grade,
            subjectId = subject
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<int> FirstLookupIdAsync(HttpClient client, string url)
    {
        var body = await client.GetFromJsonAsync<JsonElement>(url);
        return body.GetProperty("data")[0].GetProperty("id").GetInt32();
    }
}
