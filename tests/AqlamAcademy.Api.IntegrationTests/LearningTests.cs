using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class LearningTests(ApiFactory factory)
{
    [Fact]
    public async Task Student_self_enrolls_in_free_public_course_and_sees_it_in_my_courses()
    {
        var (teacher, org, courseId) = await PublishedCourseAsync("selfenr", [LessonSpec("Intro")]);
        var student = await StudentAsync("se");

        var enroll = await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/enroll", null);
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/enroll", null)).StatusCode);

        var outline = await GetDataAsync(student, $"/api/orgs/{org}/learn/courses/{courseId}");
        Assert.False(outline.GetProperty("isPreview").GetBoolean());
        Assert.Equal("Intro", outline.GetProperty("chapters")[0].GetProperty("lessons")[0].GetProperty("title").GetString());

        var myCourses = await GetDataAsync(student, "/api/me/courses");
        var mine = myCourses.EnumerateArray().Single(c => c.GetProperty("courseId").GetString() == courseId);
        Assert.Equal(org, mine.GetProperty("organizationSlug").GetString());
        Assert.Equal(0, mine.GetProperty("progressPercent").GetInt32());
    }

    [Fact]
    public async Task Draft_and_paid_courses_cannot_be_self_enrolled_and_outsiders_get_404()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "nodraft");
        var draftId = await CreateDraftAsync(teacher, org);
        var (_, paidOrg, paidId) = await PublishedCourseAsync("paid", [LessonSpec("L1")], paid: true);
        var student = await StudentAsync("nd");

        Assert.Equal(HttpStatusCode.NotFound, (await student.PostAsync($"/api/orgs/{org}/learn/courses/{draftId}/enroll", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync($"/api/orgs/{paidOrg}/learn/courses/{paidId}/enroll", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/orgs/{paidOrg}/learn/courses/{paidId}")).StatusCode);
    }

    [Fact]
    public async Task Previous_lesson_requirement_unlocks_after_watching_and_progress_is_counted_once()
    {
        var (_, org, courseId) = await PublishedCourseAsync("prereq", [LessonSpec("Part 1"), LessonSpec("Part 2", requirePrevious: true)]);
        var student = await EnrolledStudentAsync(org, courseId, "pr");
        var lessons = (await GetDataAsync(student, $"/api/orgs/{org}/learn/courses/{courseId}")).GetProperty("chapters")[0].GetProperty("lessons");
        var part1 = lessons[0].GetProperty("id").GetString();
        var part2 = lessons[1].GetProperty("id").GetString();

        Assert.True(lessons[1].GetProperty("isLocked").GetBoolean());
        Assert.Equal("PreviousLessonRequired", lessons[1].GetProperty("lockReason").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/orgs/{org}/learn/lessons/{part2}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Progress(student, org, part2!, watched: 50)).StatusCode);

        var halfway = await DataOf(await Progress(student, org, part1!, watched: 60, position: 120));
        Assert.False(halfway.GetProperty("isCompleted").GetBoolean());

        var done = await DataOf(await Progress(student, org, part1!, watched: 92, position: 300));
        Assert.True(done.GetProperty("justCompleted").GetBoolean());
        Assert.Equal(50, done.GetProperty("courseProgressPercent").GetInt32());

        var again = await DataOf(await Progress(student, org, part1!, watched: 40, position: 30));
        Assert.False(again.GetProperty("justCompleted").GetBoolean());          // completion fires once
        Assert.Equal(92, again.GetProperty("watchedPercent").GetInt32());       // never goes backwards

        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync($"/api/orgs/{org}/learn/lessons/{part2}")).StatusCode);
        var content = await GetDataAsync(student, $"/api/orgs/{org}/learn/lessons/{part2}");
        Assert.Equal(part1, content.GetProperty("previousLessonId").GetString());
    }

    [Fact]
    public async Task Drip_lessons_stay_locked_until_their_date_and_unpublished_lessons_are_hidden()
    {
        var (_, org, courseId) = await PublishedCourseAsync("drip",
            [LessonSpec("Now"), LessonSpec("Next week", unlockDays: 7), LessonSpec("Hidden draft lesson", published: false)]);
        var student = await EnrolledStudentAsync(org, courseId, "dr");

        var lessons = (await GetDataAsync(student, $"/api/orgs/{org}/learn/courses/{courseId}")).GetProperty("chapters")[0].GetProperty("lessons");

        Assert.Equal(2, lessons.GetArrayLength());
        Assert.Equal("DripSchedule", lessons[1].GetProperty("lockReason").GetString());
        Assert.True(lessons[1].GetProperty("unlocksAtUtc").GetDateTime() > DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task Completion_rules_differ_by_lesson_type()
    {
        var (_, org, courseId) = await PublishedCourseAsync("types",
            [LessonSpec("Watch me"), LessonSpec("Read me", type: "Article"), LessonSpec("Quiz", type: "Quiz")]);
        var student = await EnrolledStudentAsync(org, courseId, "ty");
        var lessons = (await GetDataAsync(student, $"/api/orgs/{org}/learn/courses/{courseId}")).GetProperty("chapters")[0].GetProperty("lessons");

        Assert.Equal(HttpStatusCode.BadRequest, (await Progress(student, org, lessons[0].GetProperty("id").GetString()!, markCompleted: true)).StatusCode);
        Assert.True((await DataOf(await Progress(student, org, lessons[1].GetProperty("id").GetString()!, markCompleted: true))).GetProperty("isCompleted").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await Progress(student, org, lessons[2].GetProperty("id").GetString()!, markCompleted: true)).StatusCode);
    }

    [Fact]
    public async Task Staff_enroll_members_preview_without_enrollment_and_expired_access_is_blocked()
    {
        var (teacher, org, courseId) = await PublishedCourseAsync("staff", [LessonSpec("L1")], visibility: "Private");
        var student = factory.CreateBrowserClient();
        var studentName = (await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, "sten")).GetProperty("data").GetProperty("userName").GetString()!;

        // Make the student a member through a join code, then enroll them as staff.
        var code = (await DataOf(await teacher.PostAsJsonAsync($"/api/orgs/{org}/join-codes", new { }))).GetProperty("code").GetString();
        (await student.PostAsJsonAsync("/api/me/join", new { code })).EnsureSuccessStatusCode();

        var results = await DataOf(await teacher.PostAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/enrollments",
            new { userNames = new[] { studentName, "no-such-user" } }));
        Assert.Equal("Enrolled", results[0].GetProperty("status").GetString());
        Assert.Equal("Failed", results[1].GetProperty("status").GetString());

        var roster = await GetDataAsync(teacher, $"/api/orgs/{org}/courses/{courseId}/enrollments");
        Assert.Equal(studentName, roster[0].GetProperty("userName").GetString());

        // Staff preview: outline without enrollment, but no progress recorded.
        var preview = await GetDataAsync(teacher, $"/api/orgs/{org}/learn/courses/{courseId}");
        Assert.True(preview.GetProperty("isPreview").GetBoolean());
        var lessonId = preview.GetProperty("chapters")[0].GetProperty("lessons")[0].GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Forbidden, (await Progress(teacher, org, lessonId, watched: 95)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync($"/api/orgs/{org}/learn/courses/{courseId}")).StatusCode);
        await ExpireEnrollmentAsync(courseId);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/orgs/{org}/learn/courses/{courseId}")).StatusCode);
        var mine = (await GetDataAsync(student, "/api/me/courses")).EnumerateArray().Single(c => c.GetProperty("courseId").GetString() == courseId);
        Assert.True(mine.GetProperty("isExpired").GetBoolean());
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private static object LessonSpec(string title, string type = "Video", bool requirePrevious = false, int unlockDays = 0, bool published = true) => new
    {
        title,
        type,
        videoUrl = type == "Video" ? "https://video.example/v" : null,
        content = type == "Article" ? "<p>Reading</p>" : null,
        isFreePreview = false,
        isPublished = published,
        unlockDaysAfterEnrollment = unlockDays,
        requirePreviousLessonCompletion = requirePrevious
    };

    /// <summary>Published course in a teacher's personal organization with the given lessons in one chapter.</summary>
    private async Task<(HttpClient Teacher, string Org, string CourseId)> PublishedCourseAsync(
        string prefix, object[] lessons, bool paid = false, string visibility = "Public")
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, prefix);
        var courseId = await CreateDraftAsync(teacher, org);
        await MakePublishableAsync(teacher, org, courseId);

        var version = (await GetDraftAsync(teacher, org, courseId)).GetProperty("curriculumVersion").GetInt32();
        (await SyncAsync(teacher, org, courseId, version, [new { title = "Unit 1", lessons }])).EnsureSuccessStatusCode();

        if (paid)
        {
            (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/commercials",
                new { id = courseId, pricingType = "Paid", price = 100m, codeActivationOnly = false, accessExpiryPolicy = "Unlimited", accessDurationDays = (int?)null }))
                .EnsureSuccessStatusCode();
        }

        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Published" })).EnsureSuccessStatusCode();
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/visibility", new { visibility })).EnsureSuccessStatusCode();
        return (teacher, org, courseId);
    }

    private async Task<HttpClient> StudentAsync(string prefix)
    {
        var student = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, prefix);
        return student;
    }

    private async Task<HttpClient> EnrolledStudentAsync(string org, string courseId, string prefix)
    {
        var student = await StudentAsync(prefix);
        (await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/enroll", null)).EnsureSuccessStatusCode();
        return student;
    }

    private static Task<HttpResponseMessage> Progress(HttpClient client, string org, string lessonId, int? watched = null, int? position = null, bool markCompleted = false) =>
        client.PostAsJsonAsync($"/api/orgs/{org}/learn/lessons/{lessonId}/progress",
            new { positionSeconds = position, watchedPercent = watched, markCompleted });

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("data");

    private static async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private async Task ExpireEnrollmentAsync(string courseId)
    {
        await using var connection = new NpgsqlConnection(factory.SharedConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE \"Learning\".\"Enrollments\" SET \"ExpiresAtUtc\" = now() - interval '1 day' WHERE \"CourseId\" = @course", connection);
        command.Parameters.AddWithValue("course", Guid.Parse(courseId));
        await command.ExecuteNonQueryAsync();
    }
}
