using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CourseClassificationTests(ApiFactory factory)
{
    [Fact]
    public async Task Draft_with_consistent_curriculum_stage_and_grade_is_accepted()
    {
        var (teacher, org) = await TeacherWithOrgAsync();
        var (curriculumId, stageId, gradeId) = await SaudiPrimaryGrade1Async(teacher);

        var response = await teacher.PostAsJsonAsync($"/api/orgs/{org}/courses/draft",
            new { title = "Science G1", curriculumId, stageId, gradeId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Stage_from_another_curriculum_and_unknown_ids_are_rejected()
    {
        var (teacher, org) = await TeacherWithOrgAsync();
        var (saudiCurriculumId, _, _) = await SaudiPrimaryGrade1Async(teacher);
        const int egyptianPrimaryStageId = 1; // seeded stage of the Egyptian National curriculum

        var mismatched = await teacher.PostAsJsonAsync($"/api/orgs/{org}/courses/draft",
            new { title = "Mixed", curriculumId = saudiCurriculumId, stageId = egyptianPrimaryStageId });
        var unknown = await teacher.PostAsJsonAsync($"/api/orgs/{org}/courses/draft",
            new { title = "Unknown", subjectId = 999999 });

        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
        Assert.True((await mismatched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("stageId", out _));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.True((await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("subjectId", out _));
    }

    private async Task<(HttpClient Teacher, string Org)> TeacherWithOrgAsync()
    {
        var teacher = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teacher, TestUsers.Teacher, "cls");
        var org = (await teacher.GetFromJsonAsync<JsonElement>("/api/me/memberships")).GetProperty("data")[0].GetProperty("slug").GetString()!;
        return (teacher, org);
    }

    private static async Task<(int CurriculumId, int StageId, int GradeId)> SaudiPrimaryGrade1Async(HttpClient client)
    {
        var curricula = (await client.GetFromJsonAsync<JsonElement>("/api/Lookups/curricula?countryCode=SA")).GetProperty("data");
        var curriculumId = curricula.EnumerateArray().First(c => c.GetProperty("code").GetString() == "SA_NATIONAL").GetProperty("id").GetInt32();
        var stages = (await client.GetFromJsonAsync<JsonElement>($"/api/Lookups/education-stages?curriculumId={curriculumId}")).GetProperty("data");
        var stageId = stages.EnumerateArray().First(s => s.GetProperty("nameEn").GetString() == "Primary").GetProperty("id").GetInt32();
        var grades = (await client.GetFromJsonAsync<JsonElement>($"/api/Lookups/education-grades?stageId={stageId}")).GetProperty("data");
        return (curriculumId, stageId, grades[0].GetProperty("id").GetInt32());
    }
}
