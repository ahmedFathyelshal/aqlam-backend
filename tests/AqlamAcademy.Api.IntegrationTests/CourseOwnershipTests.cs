using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CourseOwnershipTests(ApiFactory factory)
{
    [Fact]
    public async Task Anonymous_user_cannot_create_a_course()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.PostAsJsonAsync("/api/teacher/courses/draft", new { title = "Physics" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_use_teacher_course_endpoints()
    {
        var client = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(client, TestUsers.Student, "stu");

        var response = await client.PostAsJsonAsync("/api/teacher/courses/draft", new { title = "Physics" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_read_or_modify_another_teachers_course()
    {
        var teacherA = factory.CreateBrowserClient();
        var teacherB = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teacherA, TestUsers.Teacher, "ta");
        await TestUsers.RegisterAndLoginAsync(teacherB, TestUsers.Teacher, "tb");

        var courseId = await CreateDraftAsync(teacherA);

        var readByOwner = await teacherA.GetAsync($"/api/teacher/courses/{courseId}/draft");
        Assert.Equal(HttpStatusCode.OK, readByOwner.StatusCode);

        var readByOther = await teacherB.GetAsync($"/api/teacher/courses/{courseId}/draft");
        Assert.Equal(HttpStatusCode.NotFound, readByOther.StatusCode);

        var updateByOther = await teacherB.PutAsJsonAsync($"/api/teacher/courses/{courseId}/media", new
        {
            id = courseId,
            subtitle = "hijacked",
            thumbnailUrl = (string?)null,
            promoVideoUrl = (string?)null
        });
        Assert.Equal(HttpStatusCode.NotFound, updateByOther.StatusCode);

        var draft = await (await teacherA.GetAsync($"/api/teacher/courses/{courseId}/draft"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual("hijacked", draft.GetProperty("data").GetProperty("subtitle").GetString());
    }

    private static async Task<string> CreateDraftAsync(HttpClient teacher)
    {
        var stage = await FirstLookupIdAsync(teacher, "/api/Lookups/education-stages");
        var grade = await FirstLookupIdAsync(teacher, $"/api/Lookups/education-grades?stageId={stage}");
        var subject = await FirstLookupIdAsync(teacher, "/api/Lookups/subjects");

        var response = await teacher.PostAsJsonAsync("/api/teacher/courses/draft", new
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
