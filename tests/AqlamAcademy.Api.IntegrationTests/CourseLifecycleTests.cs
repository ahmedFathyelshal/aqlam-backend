using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CourseLifecycleTests(ApiFactory factory)
{
    [Fact]
    public async Task Incomplete_course_cannot_be_published_and_the_checklist_explains_why()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "pub");
        var courseId = await CreateDraftAsync(teacher, org);

        var checklist = (await teacher.GetFromJsonAsync<JsonElement>($"/api/orgs/{org}/courses/{courseId}/publish-checklist")).GetProperty("data");
        var publish = await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Published" });

        Assert.Contains(checklist.EnumerateArray(), i => i.GetString()!.Contains("lesson"));
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
    }

    [Fact]
    public async Task Complete_course_publishes_and_invalid_transitions_are_rejected()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "pub2");
        var courseId = await CreateDraftAsync(teacher, org);
        await MakePublishableAsync(teacher, org, courseId);

        var checklist = (await teacher.GetFromJsonAsync<JsonElement>($"/api/orgs/{org}/courses/{courseId}/publish-checklist")).GetProperty("data");
        Assert.Equal(0, checklist.GetArrayLength());

        var publish = await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Published" });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal("Published", (await publish.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("status").GetString());

        var toScheduled = await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Scheduled" });
        var toSuspended = await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Suspended" });
        Assert.Equal(HttpStatusCode.Conflict, toScheduled.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toSuspended.StatusCode);

        var archive = await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Archived" });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var list = (await teacher.GetFromJsonAsync<JsonElement>($"/api/orgs/{org}/courses")).GetProperty("data");
        var row = list.EnumerateArray().Single(c => c.GetProperty("id").GetString() == courseId);
        Assert.Equal("Archived", row.GetProperty("status").GetString());
        Assert.Equal(1, row.GetProperty("lessonCount").GetInt32());
    }

    [Fact]
    public async Task School_teacher_cannot_publish_and_school_courses_cannot_go_public()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();
        var owner = factory.CreateBrowserClient();
        var ownerName = (await TestUsers.RegisterAndLoginAsync(owner, TestUsers.Teacher, "sown")).GetProperty("data").GetProperty("userName").GetString()!;
        var teacher = factory.CreateBrowserClient();
        var teacherName = (await TestUsers.RegisterAndLoginAsync(teacher, TestUsers.Teacher, "stch")).GetProperty("data").GetProperty("userName").GetString()!;
        var slug = $"school-{Guid.NewGuid():N}"[..20];
        (await admin.PostAsJsonAsync("/api/platform/organizations", new { name = "Pilot School", slug, type = "School", countryCode = "SA", ownerUserName = ownerName })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/orgs/{slug}/members", new { userName = teacherName, roles = new[] { "Teacher" } })).EnsureSuccessStatusCode();

        var courseId = await CreateDraftAsync(teacher, slug);
        await MakePublishableAsync(teacher, slug, courseId);

        var teacherPublish = await teacher.PutAsJsonAsync($"/api/orgs/{slug}/courses/{courseId}/status", new { status = "Published" });
        var ownerPublish = await owner.PutAsJsonAsync($"/api/orgs/{slug}/courses/{courseId}/status", new { status = "Published" });
        var ownerPublic = await owner.PutAsJsonAsync($"/api/orgs/{slug}/courses/{courseId}/visibility", new { visibility = "Public" });
        var ownerUnlisted = await owner.PutAsJsonAsync($"/api/orgs/{slug}/courses/{courseId}/visibility", new { visibility = "Unlisted" });

        Assert.Equal(HttpStatusCode.Forbidden, teacherPublish.StatusCode);   // teachers prepare, admins publish
        Assert.Equal(HttpStatusCode.OK, ownerPublish.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ownerPublic.StatusCode);      // schools are private by default
        Assert.Equal(HttpStatusCode.OK, ownerUnlisted.StatusCode);
    }

    [Fact]
    public async Task Independent_teacher_can_make_a_course_public()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "pubvis");
        var courseId = await CreateDraftAsync(teacher, org);

        var response = await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/visibility", new { visibility = "Public" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Public", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("visibility").GetString());
    }
}
