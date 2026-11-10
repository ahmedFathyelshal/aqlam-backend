using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static AqlamAcademy.Api.IntegrationTests.TestSchool;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CourseAccessTests(ApiFactory factory)
{
    [Fact]
    public async Task School_teachers_work_only_on_the_subjects_assigned_to_them()
    {
        var school = await TestSchool.CreateAsync(factory);
        var classId = await school.CreateClassAsync("8-A");
        var science = await school.AddMemberAsync("Teacher", prefix: "sci");
        var other = await school.AddMemberAsync("Teacher", prefix: "oth");
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments", new { subjectName = "Science", teacherUserId = science.UserId })).EnsureSuccessStatusCode();

        // The school's subject created the course; the assigned teacher sees and edits it.
        var mine = (await GetDataAsync(science.Client, $"{school.Api}/courses")).EnumerateArray().ToList();
        var courseId = Assert.Single(mine).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await science.Client.GetAsync($"{school.Api}/courses/{courseId}/draft")).StatusCode);

        // Another teacher neither sees nor opens it, and cannot create loose courses in a school.
        Assert.Equal(0, (await GetDataAsync(other.Client, $"{school.Api}/courses")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync($"{school.Api}/courses/{courseId}/draft")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.PostAsJsonAsync($"{school.Api}/courses/draft", new { title = "Loose" })).StatusCode);

        // A school cannot switch teacher course creation on; only centers choose.
        var saved = await DataAsync(await school.Owner.PutAsJsonAsync($"{school.Api}/settings",
            new { name = "Al Rowad", timeZone = "Asia/Riyadh", calendar = "Both", weekStartDay = "Sunday", teachersCreateCourses = true }));
        Assert.False(saved.GetProperty("teachersCreateCourses").GetBoolean());

        // The owner sees every course.
        Assert.Equal(1, (await GetDataAsync(school.Owner, $"{school.Api}/courses")).GetArrayLength());
    }
}
