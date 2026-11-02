using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

/// <summary>Shared helpers for course authoring tests.</summary>
public static class CourseTestHelpers
{
    public static async Task<(HttpClient Teacher, string Org)> TeacherWithPersonalOrgAsync(ApiFactory factory, string prefix)
    {
        var teacher = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teacher, TestUsers.Teacher, prefix);
        var org = (await teacher.GetFromJsonAsync<JsonElement>("/api/me/memberships")).GetProperty("data")[0].GetProperty("slug").GetString()!;
        return (teacher, org);
    }

    public static async Task<string> CreateDraftAsync(HttpClient client, string org, string title = "Physics 101")
    {
        var response = await client.PostAsJsonAsync($"/api/orgs/{org}/courses/draft", new { title });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetString()!;
    }

    public static async Task<JsonElement> GetDraftAsync(HttpClient client, string org, string courseId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/orgs/{org}/courses/{courseId}/draft")).GetProperty("data");

    public static Task<HttpResponseMessage> SyncAsync(HttpClient client, string org, string courseId, int? expectedVersion, object[] chapters) =>
        client.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/curriculum", new { expectedVersion, chapters });

    public static object Lesson(string title, string? id = null, string type = "Video", string? videoUrl = "https://video.example/1", object[]? resources = null) => new
    {
        id,
        title,
        type,
        videoUrl,
        isFreePreview = false,
        isPublished = true,
        unlockDaysAfterEnrollment = 0,
        requirePreviousLessonCompletion = false,
        resources
    };

    /// <summary>Fills everything the publish checklist needs (classification, pricing, access, one video lesson).</summary>
    public static async Task MakePublishableAsync(HttpClient client, string org, string courseId)
    {
        var curricula = (await client.GetFromJsonAsync<JsonElement>("/api/Lookups/curricula?countryCode=SA")).GetProperty("data");
        var curriculumId = curricula.EnumerateArray().First(c => c.GetProperty("code").GetString() == "SA_NATIONAL").GetProperty("id").GetInt32();
        var stageId = (await client.GetFromJsonAsync<JsonElement>($"/api/Lookups/education-stages?curriculumId={curriculumId}")).GetProperty("data")[1].GetProperty("id").GetInt32();
        var gradeId = (await client.GetFromJsonAsync<JsonElement>($"/api/Lookups/education-grades?stageId={stageId}")).GetProperty("data")[0].GetProperty("id").GetInt32();
        var subjectId = (await client.GetFromJsonAsync<JsonElement>("/api/Lookups/subjects")).GetProperty("data")[0].GetProperty("id").GetInt32();

        (await client.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/identity",
            new { id = courseId, title = "Complete Course", curriculumId, stageId, gradeId, subjectId })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/commercials",
            new { id = courseId, pricingType = "Free", price = (decimal?)null, codeActivationOnly = false, accessExpiryPolicy = "Unlimited", accessDurationDays = (int?)null }))
            .EnsureSuccessStatusCode();

        var version = (await GetDraftAsync(client, org, courseId)).GetProperty("curriculumVersion").GetInt32();
        (await SyncAsync(client, org, courseId, version, [new { title = "Unit 1", lessons = new[] { Lesson("Welcome") } }])).EnsureSuccessStatusCode();
    }
}
