using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AssignmentTests(ApiFactory factory)
{
    [Fact]
    public async Task Submit_grade_return_for_revision_resubmit_and_final_grade()
    {
        var (teacher, org, courseId, lessonId) = await AssignmentCourseAsync("asg", maxPoints: 20, dueAtUtc: null, acceptLate: true);
        var student = await EnrolledStudentAsync(org, courseId, "as");

        var view = await DataOf(await student.GetAsync($"/api/orgs/{org}/learn/lessons/{lessonId}/assignment"));
        Assert.True(view.GetProperty("canSubmit").GetBoolean());
        Assert.Equal(20m, view.GetProperty("maxPoints").GetDecimal());

        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(student, org, lessonId, text: null, links: [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(student, org, lessonId, text: null, links: ["http://insecure.example/work"])).StatusCode);

        var submitted = await DataOf(await Submit(student, org, lessonId, "My essay", ["https://docs.example/essay"]));
        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());

        // Handing in completes the lesson.
        var outline = await DataOf(await student.GetAsync($"/api/orgs/{org}/learn/courses/{courseId}"));
        Assert.True(outline.GetProperty("chapters")[0].GetProperty("lessons").EnumerateArray()
            .Single(l => l.GetProperty("id").GetString() == lessonId).GetProperty("isCompleted").GetBoolean());

        var queue = await DataOf(await teacher.GetAsync($"/api/orgs/{org}/courses/{courseId}/submissions?pending=true"));
        var row = Assert.Single(queue.EnumerateArray());
        Assert.NotNull(row.GetProperty("studentUserName").GetString());
        var submissionId = row.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.BadRequest, (await Grade(teacher, org, submissionId!, score: 25)).StatusCode);   // above max
        Assert.Equal(HttpStatusCode.BadRequest, (await Grade(teacher, org, submissionId!, score: null, returnForRevision: true)).StatusCode); // feedback required

        var returned = await DataOf(await Grade(teacher, org, submissionId!, score: null, feedback: "Add a conclusion.", returnForRevision: true));
        Assert.Equal("ReturnedForRevision", returned.GetProperty("status").GetString());

        var revised = await DataOf(await Submit(student, org, lessonId, "My essay with a conclusion", []));
        Assert.Equal("Submitted", revised.GetProperty("status").GetString());

        var graded = await DataOf(await Grade(teacher, org, submissionId!, score: 18, feedback: "Well done"));
        Assert.Equal("Graded", graded.GetProperty("status").GetString());
        Assert.Equal(18m, graded.GetProperty("score").GetDecimal());

        Assert.Equal(HttpStatusCode.Conflict, (await Submit(student, org, lessonId, "Too late to change", [])).StatusCode);
        Assert.Equal(0, (await DataOf(await teacher.GetAsync($"/api/orgs/{org}/courses/{courseId}/submissions?pending=true"))).GetArrayLength());
    }

    [Fact]
    public async Task Due_dates_and_late_submissions()
    {
        var past = DateTime.UtcNow.AddDays(-1);
        var (_, strictOrg, strictCourse, strictLesson) = await AssignmentCourseAsync("asgstrict", maxPoints: 10, dueAtUtc: past, acceptLate: false);
        var (_, lenientOrg, lenientCourse, lenientLesson) = await AssignmentCourseAsync("asglate", maxPoints: 10, dueAtUtc: past, acceptLate: true);

        var strictStudent = await EnrolledStudentAsync(strictOrg, strictCourse, "ss");
        var lenientStudent = await EnrolledStudentAsync(lenientOrg, lenientCourse, "sl");

        Assert.False((await DataOf(await strictStudent.GetAsync($"/api/orgs/{strictOrg}/learn/lessons/{strictLesson}/assignment"))).GetProperty("canSubmit").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(strictStudent, strictOrg, strictLesson, "late work", [])).StatusCode);

        var late = await DataOf(await Submit(lenientStudent, lenientOrg, lenientLesson, "late work", []));
        Assert.True(late.GetProperty("isLate").GetBoolean());
    }

    [Fact]
    public async Task Assistants_cannot_grade_and_settings_only_apply_to_assignment_lessons()
    {
        var (teacher, org, courseId, lessonId) = await AssignmentCourseAsync("asgperm", maxPoints: 10, dueAtUtc: null, acceptLate: true);
        var student = await EnrolledStudentAsync(org, courseId, "sp");
        var submissionId = (await DataOf(await Submit(student, org, lessonId, "work", []))).GetProperty("id").GetString()!;

        var assistant = factory.CreateBrowserClient();
        var assistantName = (await TestUsers.RegisterAndLoginAsync(assistant, TestUsers.Teacher, "asst")).GetProperty("data").GetProperty("userName").GetString();
        (await teacher.PostAsJsonAsync($"/api/orgs/{org}/members", new { userName = assistantName, roles = new[] { "Assistant" } })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await Grade(assistant, org, submissionId, score: 5)).StatusCode);

        var videoLesson = (await GetDraftAsync(teacher, org, courseId)).GetProperty("chapters")[0].GetProperty("lessons")[0].GetProperty("id").GetString();
        var onVideo = await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/lessons/{videoLesson}/assignment", new { maxPoints = 10 });
        Assert.Equal(HttpStatusCode.Conflict, onVideo.StatusCode);
    }

    [Fact]
    public async Task Student_home_shows_next_lesson_work_due_and_latest_result()
    {
        var (teacher, org, courseId, lessonId) = await AssignmentCourseAsync("home", maxPoints: 20, DateTime.UtcNow.AddDays(2), acceptLate: true);
        var student = await EnrolledStudentAsync(org, courseId, "hs");

        var home = await DataOf(await student.GetAsync($"/api/orgs/{org}/learn/home"));
        var next = Assert.Single(home.GetProperty("nextLessons").EnumerateArray());
        Assert.Equal("Intro video", next.GetProperty("title").GetString());
        Assert.Equal("Unit 1", next.GetProperty("chapterTitle").GetString());
        var due = Assert.Single(home.GetProperty("due").EnumerateArray());
        Assert.Equal(lessonId, due.GetProperty("lessonId").GetString());
        Assert.False(due.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, home.GetProperty("latestResult").ValueKind);
        Assert.Equal(85, home.GetProperty("achievementPercent").GetInt32());

        // Handed in: no longer due. Graded: becomes the latest result.
        (await Submit(student, org, lessonId, "My essay", [])).EnsureSuccessStatusCode();
        Assert.Empty((await DataOf(await student.GetAsync($"/api/orgs/{org}/learn/home"))).GetProperty("due").EnumerateArray());

        var queue = await DataOf(await teacher.GetAsync($"/api/orgs/{org}/courses/{courseId}/submissions?pending=true"));
        (await Grade(teacher, org, queue[0].GetProperty("id").GetString()!, score: 18)).EnsureSuccessStatusCode();
        var result = (await DataOf(await student.GetAsync($"/api/orgs/{org}/learn/home"))).GetProperty("latestResult");
        Assert.Equal("Assignment", result.GetProperty("kind").GetString());
        Assert.Equal(90m, result.GetProperty("percent").GetDecimal());

        // Another student's home is their own.
        var other = await EnrolledStudentAsync(org, courseId, "ho");
        var otherHome = await DataOf(await other.GetAsync($"/api/orgs/{org}/learn/home"));
        Assert.Equal(JsonValueKind.Null, otherHome.GetProperty("latestResult").ValueKind);
        Assert.Single(otherHome.GetProperty("due").EnumerateArray());
    }

    [Fact]
    public async Task Student_home_leaves_out_overdue_work_that_is_no_longer_accepted()
    {
        var (_, org, courseId, _) = await AssignmentCourseAsync("homel", maxPoints: 10, DateTime.UtcNow.AddDays(-2), acceptLate: false);
        var student = await EnrolledStudentAsync(org, courseId, "hl");

        var home = await DataOf(await student.GetAsync($"/api/orgs/{org}/learn/home"));
        Assert.Empty(home.GetProperty("due").EnumerateArray());
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_teacher_cannot_grade_work_in_another_teachers_course_by_its_id()
    {
        var center = await TestSchool.CreateAsync(factory, "Center");
        (await center.Owner.PutAsJsonAsync($"{center.Api}/settings",
            new { name = "Tamayoz", timeZone = "Africa/Cairo", calendar = "Gregorian", weekStartDay = "Saturday", teachersCreateCourses = true })).EnsureSuccessStatusCode();
        var author = await center.AddMemberAsync("Teacher", prefix: "auth");
        var outsider = await center.AddMemberAsync("Teacher", prefix: "outs");

        var (_, _, courseId, lessonId) = await AssignmentCourseAsync(author.Client, center.Owner, center.Slug, maxPoints: 10, dueAtUtc: null, acceptLate: true);
        var student = await EnrolledStudentAsync(center.Slug, courseId, "cs");
        var submissionId = (await DataOf(await Submit(student, center.Slug, lessonId, "work", []))).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await Grade(outsider.Client, center.Slug, submissionId, score: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Grade(author.Client, center.Slug, submissionId, score: 9)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Grade(center.Owner, center.Slug, submissionId, score: 8)).StatusCode);
    }

    private async Task<(HttpClient Teacher, string Org, string CourseId, string LessonId)> AssignmentCourseAsync(
        string prefix, decimal maxPoints, DateTime? dueAtUtc, bool acceptLate)
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, prefix);
        return await AssignmentCourseAsync(teacher, teacher, org, maxPoints, dueAtUtc, acceptLate);
    }

    /// <summary>The author builds the course; the publisher (who may be the author) puts it live and public.</summary>
    private static async Task<(HttpClient Teacher, string Org, string CourseId, string LessonId)> AssignmentCourseAsync(
        HttpClient teacher, HttpClient publisher, string org, decimal maxPoints, DateTime? dueAtUtc, bool acceptLate)
    {
        var courseId = await CreateDraftAsync(teacher, org);
        await MakePublishableAsync(teacher, org, courseId);

        var version = (await GetDraftAsync(teacher, org, courseId)).GetProperty("curriculumVersion").GetInt32();
        var tree = await DataOf(await SyncAsync(teacher, org, courseId, version,
            [new { title = "Unit 1", lessons = new[] { Lesson("Intro video"), Lesson("Essay", type: "Assignment", videoUrl: null) } }]));
        var lessonId = tree.GetProperty("chapters")[0].GetProperty("lessons")[1].GetProperty("id").GetString()!;

        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/lessons/{lessonId}/assignment",
            new { maxPoints, dueAtUtc, acceptLateSubmissions = acceptLate })).EnsureSuccessStatusCode();
        (await publisher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Published" })).EnsureSuccessStatusCode();
        (await publisher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/visibility", new { visibility = "Public" })).EnsureSuccessStatusCode();
        return (teacher, org, courseId, lessonId);
    }

    private async Task<HttpClient> EnrolledStudentAsync(string org, string courseId, string prefix)
    {
        var student = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, prefix);
        (await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/enroll", null)).EnsureSuccessStatusCode();
        return student;
    }

    private static Task<HttpResponseMessage> Submit(HttpClient client, string org, string lessonId, string? text, string[] links) =>
        client.PutAsJsonAsync($"/api/orgs/{org}/learn/lessons/{lessonId}/assignment/submission", new { text, links });

    private static Task<HttpResponseMessage> Grade(HttpClient client, string org, string submissionId, decimal? score, string? feedback = null, bool returnForRevision = false) =>
        client.PutAsJsonAsync($"/api/orgs/{org}/submissions/{submissionId}/grade", new { score, feedback, returnForRevision });

    private static async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
