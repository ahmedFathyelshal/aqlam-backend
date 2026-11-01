using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class QuizTests(ApiFactory factory)
{
    [Fact]
    public async Task Invalid_questions_are_rejected()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "qbad");

        var twoCorrect = await teacher.PostAsJsonAsync($"/api/orgs/{org}/questions", new
        {
            type = "SingleChoice", text = "2+2?", points = 1, difficulty = 1,
            options = new[] { new { text = "4", isCorrect = true }, new { text = "four", isCorrect = true } }
        });
        var noAnswers = await teacher.PostAsJsonAsync($"/api/orgs/{org}/questions", new { type = "ShortAnswer", text = "Capital?", points = 1, difficulty = 1, acceptedAnswers = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, twoCorrect.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noAnswers.StatusCode);
    }

    [Fact]
    public async Task Full_quiz_flow_hides_answers_autosaves_grades_with_arabic_normalisation_and_completes_the_lesson()
    {
        var setup = await QuizCourseAsync("qflow", settings: new { passPercent = 60, showAnswersAfterSubmit = true });
        var (student, org) = (await EnrolledStudentAsync(setup.Org, setup.CourseId, "qs"), setup.Org);

        var summary = await DataOf(await student.GetAsync($"/api/orgs/{org}/learn/lessons/{setup.QuizLessonId}/quiz"));
        Assert.Equal(3, summary.GetProperty("questionCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("attemptsUsed").GetInt32());

        var start = await student.PostAsync($"/api/orgs/{org}/learn/lessons/{setup.QuizLessonId}/quiz/attempts", null);
        var startJson = await start.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.DoesNotContain("isCorrect", startJson, StringComparison.OrdinalIgnoreCase);       // answers never leak
        Assert.DoesNotContain("acceptedAnswers", startJson, StringComparison.OrdinalIgnoreCase);
        var attemptId = JsonDocument.Parse(startJson).RootElement.GetProperty("data").GetProperty("id").GetString();

        // Autosave one answer, then resuming returns the same attempt with the saved answer.
        (await student.PutAsJsonAsync($"/api/orgs/{org}/learn/attempts/{attemptId}/answers",
            new { answers = new[] { new { questionId = setup.SingleId, selectedOptionIds = new[] { setup.SingleCorrectOptionId } } } })).EnsureSuccessStatusCode();
        var resumed = await DataOf(await student.PostAsync($"/api/orgs/{org}/learn/lessons/{setup.QuizLessonId}/quiz/attempts", null));
        Assert.Equal(attemptId, resumed.GetProperty("id").GetString());

        var submit = await DataOf(await student.PostAsJsonAsync($"/api/orgs/{org}/learn/attempts/{attemptId}/submit", new
        {
            answers = new object[]
            {
                new { questionId = setup.MultiId, selectedOptionIds = setup.MultiCorrectOptionIds },
                new { questionId = setup.ShortId, textAnswer = "  أحمـد  " } // hamza + tatweel variant of "احمد"
            }
        }));

        Assert.Equal(100m, submit.GetProperty("percent").GetDecimal());
        Assert.True(submit.GetProperty("passed").GetBoolean());
        Assert.True(submit.GetProperty("lessonCompleted").GetBoolean());
        Assert.Equal(3, submit.GetProperty("items").GetArrayLength());

        var after = await DataOf(await student.GetAsync($"/api/orgs/{org}/learn/lessons/{setup.QuizLessonId}/quiz"));
        Assert.Equal(1, after.GetProperty("attemptsUsed").GetInt32());
        Assert.True(after.GetProperty("passed").GetBoolean());

        var outline = await DataOf(await student.GetAsync($"/api/orgs/{org}/learn/courses/{setup.CourseId}"));
        var quizLesson = outline.GetProperty("chapters")[0].GetProperty("lessons").EnumerateArray().Single(l => l.GetProperty("id").GetString() == setup.QuizLessonId);
        Assert.True(quizLesson.GetProperty("isCompleted").GetBoolean());
    }

    [Fact]
    public async Task Failing_does_not_complete_the_lesson_and_the_attempt_limit_is_enforced()
    {
        var setup = await QuizCourseAsync("qlimit", settings: new { passPercent = 60, maxAttempts = 1, showAnswersAfterSubmit = false });
        var student = await EnrolledStudentAsync(setup.Org, setup.CourseId, "ql");

        var attemptId = (await DataOf(await student.PostAsync($"/api/orgs/{setup.Org}/learn/lessons/{setup.QuizLessonId}/quiz/attempts", null))).GetProperty("id").GetString();
        var result = await DataOf(await student.PostAsJsonAsync($"/api/orgs/{setup.Org}/learn/attempts/{attemptId}/submit", new { answers = Array.Empty<object>() }));

        Assert.False(result.GetProperty("passed").GetBoolean());
        Assert.False(result.GetProperty("lessonCompleted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("items").ValueKind); // answers hidden by quiz setting
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsync($"/api/orgs/{setup.Org}/learn/lessons/{setup.QuizLessonId}/quiz/attempts", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync($"/api/orgs/{setup.Org}/learn/attempts/{attemptId}/submit", new { })).StatusCode);
    }

    [Fact]
    public async Task Server_timer_rejects_late_answers_and_auto_submits()
    {
        var setup = await QuizCourseAsync("qtimer", settings: new { passPercent = 60, timeLimitMinutes = 5, showAnswersAfterSubmit = true });
        var student = await EnrolledStudentAsync(setup.Org, setup.CourseId, "qt");
        var attemptId = (await DataOf(await student.PostAsync($"/api/orgs/{setup.Org}/learn/lessons/{setup.QuizLessonId}/quiz/attempts", null))).GetProperty("id").GetString()!;

        await ExecuteAsync("UPDATE \"Assessments\".\"QuizAttempts\" SET \"DeadlineUtc\" = now() - interval '5 minutes' WHERE \"Id\" = @id", Guid.Parse(attemptId));

        var late = await student.PutAsJsonAsync($"/api/orgs/{setup.Org}/learn/attempts/{attemptId}/answers",
            new { answers = new[] { new { questionId = setup.SingleId, selectedOptionIds = new[] { setup.SingleCorrectOptionId } } } });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);

        var summary = await DataOf(await student.GetAsync($"/api/orgs/{setup.Org}/learn/lessons/{setup.QuizLessonId}/quiz"));
        Assert.Equal(1, summary.GetProperty("attemptsUsed").GetInt32());
        Assert.Equal(0m, summary.GetProperty("bestPercent").GetDecimal()); // the late answer was not counted
    }

    [Fact]
    public async Task Invalid_answers_staff_preview_and_quiz_on_non_quiz_lesson_are_rejected()
    {
        var setup = await QuizCourseAsync("qrules", settings: new { passPercent = 60, showAnswersAfterSubmit = true });
        var student = await EnrolledStudentAsync(setup.Org, setup.CourseId, "qr");
        var attemptId = (await DataOf(await student.PostAsync($"/api/orgs/{setup.Org}/learn/lessons/{setup.QuizLessonId}/quiz/attempts", null))).GetProperty("id").GetString();

        var foreignOption = await student.PutAsJsonAsync($"/api/orgs/{setup.Org}/learn/attempts/{attemptId}/answers",
            new { answers = new[] { new { questionId = setup.SingleId, selectedOptionIds = new[] { Guid.NewGuid().ToString() } } } });
        var staffStart = await setup.Teacher.PostAsync($"/api/orgs/{setup.Org}/learn/lessons/{setup.QuizLessonId}/quiz/attempts", null);
        var quizOnVideo = await setup.Teacher.PutAsJsonAsync($"/api/orgs/{setup.Org}/courses/{setup.CourseId}/lessons/{setup.VideoLessonId}/quiz",
            new { passPercent = 50, questions = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, foreignOption.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffStart.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, quizOnVideo.StatusCode);
    }

    // ---- setup ---------------------------------------------------------------------------------------------

    private sealed record QuizSetup(
        HttpClient Teacher, string Org, string CourseId, string QuizLessonId, string VideoLessonId,
        string SingleId, string SingleCorrectOptionId, string MultiId, string[] MultiCorrectOptionIds, string ShortId);

    /// <summary>
    /// Published free course with a video lesson and a quiz lesson holding three questions
    /// (single choice, multiple choice, Arabic short answer). Publishing is verified to require the quiz.
    /// </summary>
    private async Task<QuizSetup> QuizCourseAsync(string prefix, object settings)
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, prefix);
        var courseId = await CreateDraftAsync(teacher, org);
        await MakePublishableAsync(teacher, org, courseId);

        var version = (await GetDraftAsync(teacher, org, courseId)).GetProperty("curriculumVersion").GetInt32();
        var tree = await DataOf(await SyncAsync(teacher, org, courseId, version,
            [new { title = "Unit 1", lessons = new[] { Lesson("Video"), Lesson("Check yourself", type: "Quiz", videoUrl: null) } }]));
        var lessons = tree.GetProperty("chapters")[0].GetProperty("lessons");
        var videoLessonId = lessons[0].GetProperty("id").GetString()!;
        var quizLessonId = lessons[1].GetProperty("id").GetString()!;

        // Publishing is blocked while the quiz has no questions.
        var blocked = await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Published" });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("has no questions", await blocked.Content.ReadAsStringAsync());

        var single = await DataOf(await teacher.PostAsJsonAsync($"/api/orgs/{org}/questions", new
        {
            type = "SingleChoice", text = "2 + 2 = ?", points = 1, difficulty = 1,
            options = new[] { new { text = "3", isCorrect = false }, new { text = "4", isCorrect = true } }
        }));
        var multi = await DataOf(await teacher.PostAsJsonAsync($"/api/orgs/{org}/questions", new
        {
            type = "MultipleChoice", text = "Even numbers?", points = 2, difficulty = 2,
            options = new[] { new { text = "2", isCorrect = true }, new { text = "3", isCorrect = false }, new { text = "4", isCorrect = true } }
        }));
        var shortAnswer = await DataOf(await teacher.PostAsJsonAsync($"/api/orgs/{org}/questions", new
        {
            type = "ShortAnswer", text = "اكتب اسم الطالب", points = 1, difficulty = 1, acceptedAnswers = new[] { "احمد" }
        }));

        var quiz = new Dictionary<string, object?>(((JsonElement)JsonSerializer.SerializeToElement(settings)).EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value))
        {
            ["questions"] = new[]
            {
                new { questionId = single.GetProperty("id").GetString() },
                new { questionId = multi.GetProperty("id").GetString() },
                new { questionId = shortAnswer.GetProperty("id").GetString() }
            }
        };
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/lessons/{quizLessonId}/quiz", quiz)).EnsureSuccessStatusCode();

        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Published" })).EnsureSuccessStatusCode();
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/visibility", new { visibility = "Public" })).EnsureSuccessStatusCode();

        string CorrectOf(JsonElement question) =>
            question.GetProperty("options").EnumerateArray().First(o => o.GetProperty("isCorrect").GetBoolean()).GetProperty("id").GetString()!;

        return new QuizSetup(teacher, org, courseId, quizLessonId, videoLessonId,
            single.GetProperty("id").GetString()!, CorrectOf(single),
            multi.GetProperty("id").GetString()!,
            multi.GetProperty("options").EnumerateArray().Where(o => o.GetProperty("isCorrect").GetBoolean()).Select(o => o.GetProperty("id").GetString()!).ToArray(),
            shortAnswer.GetProperty("id").GetString()!);
    }

    private async Task<HttpClient> EnrolledStudentAsync(string org, string courseId, string prefix)
    {
        var student = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, prefix);
        (await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/enroll", null)).EnsureSuccessStatusCode();
        return student;
    }

    private static async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(factory.SharedConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }
}
