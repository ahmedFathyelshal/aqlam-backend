using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;
using static AqlamAcademy.Api.IntegrationTests.TestSchool;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AcademicsTests(ApiFactory factory)
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static string Day(int offset) => Now.AddDays(offset).ToString("yyyy-MM-dd");
    private static readonly string Today = Day(0);

    private static object YearBody(string name = "1448") => new
    {
        name,
        startDate = Day(-30),
        endDate = Day(300),
        terms = new[]
        {
            new { name = "First semester", startDate = Day(-30), endDate = Day(100) },
            new { name = "Second semester", startDate = Day(101), endDate = Day(300) }
        }
    };

    [Fact]
    public async Task First_year_is_current_new_classes_join_it_and_years_with_classes_stay()
    {
        var school = await TestSchool.CreateAsync(factory);
        var teacher = await school.AddMemberAsync("Teacher", prefix: "yt");
        var student = await school.AddMemberAsync("Student", prefix: "ys");
        var years = $"{school.Api}/academic-years";

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync(years, YearBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.Client.GetAsync(years)).StatusCode);

        var olderClass = await school.CreateClassAsync("6-A"); // before any year exists

        var first = await DataAsync(await school.Owner.PostAsJsonAsync(years, YearBody()));
        var firstId = first.GetProperty("id").GetString();
        Assert.Equal(firstId, (await GetDataAsync(school.Owner, $"{school.Api}/classes/{olderClass}")).GetProperty("class").GetProperty("academicYearId").GetString());
        Assert.True(first.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(2, first.GetProperty("terms").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.PostAsJsonAsync(years, YearBody())).StatusCode);

        var second = await DataAsync(await school.Owner.PostAsJsonAsync(years, YearBody("1449")));
        var secondId = second.GetProperty("id").GetString();
        Assert.False(second.GetProperty("isCurrent").GetBoolean());

        var classId = await school.CreateClassAsync("7-A");
        var detail = await GetDataAsync(school.Owner, $"{school.Api}/classes/{classId}");
        Assert.Equal(firstId, detail.GetProperty("class").GetProperty("academicYearId").GetString());
        Assert.Equal(2, (await GetDataAsync(school.Owner, $"{school.Api}/classes?yearId={firstId}")).GetArrayLength());
        Assert.Equal(0, (await GetDataAsync(school.Owner, $"{school.Api}/classes?yearId={secondId}")).GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await school.Owner.PostAsync($"{years}/{secondId}/current", null)).StatusCode);
        var list = await GetDataAsync(school.Owner, years);
        Assert.Equal(1, list.EnumerateArray().Count(y => y.GetProperty("isCurrent").GetBoolean()));
        Assert.True(list.EnumerateArray().Single(y => y.GetProperty("id").GetString() == secondId).GetProperty("isCurrent").GetBoolean());

        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.DeleteAsync($"{years}/{firstId}")).StatusCode);
    }

    [Fact]
    public async Task Weighted_grades_only_count_graded_categories_and_scores_are_checked()
    {
        var g = await GradebookAsync();

        var quizzes = await AddCategoryAsync(g, "Quizzes", 40);
        var exam = await AddCategoryAsync(g, "Final exam", 60);
        var quiz1 = await AddItemAsync(g, quizzes, "Quiz 1", 20);
        var final = await AddItemAsync(g, exam, "Final", 100);

        Assert.Equal(HttpStatusCode.OK, (await Scores(g, quiz1, (g.Students[0].UserId, 15m), (g.Students[1].UserId, 10m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Scores(g, final, (g.Students[0].UserId, 90m))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await Scores(g, quiz1, (g.Students[0].UserId, 21m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Scores(g, quiz1, (g.Students[0].UserId, -1m))).StatusCode);
        var outsider = await g.School.AddMemberAsync("Student", prefix: "out");
        Assert.Equal(HttpStatusCode.BadRequest, (await Scores(g, quiz1, (outsider.UserId, 5m))).StatusCode);

        var book = await GetDataAsync(g.Teacher.Client, $"{g.Url}?termId={g.TermId}");
        Assert.Equal(100m, book.GetProperty("weightsTotal").GetDecimal());
        Assert.Equal("Descriptive", book.GetProperty("scale").GetString());

        // 40% of 75 + 60% of 90
        var top = Row(book, g.Students[0].UserId);
        Assert.Equal(84m, top.GetProperty("finalPercent").GetDecimal());
        Assert.Equal("VeryGood", top.GetProperty("label").GetString());
        // Nothing yet in the exam: judged on what has been graded, not punished with a zero.
        var partial = Row(book, g.Students[1].UserId);
        Assert.Equal(50m, partial.GetProperty("finalPercent").GetDecimal());
        Assert.Equal("Fail", partial.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, Row(book, g.Students[2].UserId).GetProperty("finalPercent").ValueKind);

        // Clearing a score removes it from the average.
        (await Scores(g, final, (g.Students[0].UserId, null))).EnsureSuccessStatusCode();
        var cleared = await GetDataAsync(g.Teacher.Client, $"{g.Url}?termId={g.TermId}");
        Assert.Equal(75m, Row(cleared, g.Students[0].UserId).GetProperty("finalPercent").GetDecimal());

        // A category with items cannot be deleted; an empty one can.
        Assert.Equal(HttpStatusCode.Conflict, (await g.Teacher.Client.DeleteAsync($"{g.Url}/categories/{quizzes}")).StatusCode);
        var empty = await AddCategoryAsync(g, "Unused", 5);
        Assert.Equal(HttpStatusCode.OK, (await g.Teacher.Client.DeleteAsync($"{g.Url}/categories/{empty}")).StatusCode);
    }

    [Fact]
    public async Task Only_the_subject_teacher_and_class_managers_reach_a_gradebook()
    {
        var g = await GradebookAsync();
        var other = await g.School.AddMemberAsync("Teacher", prefix: "oth");
        var book = $"{g.Url}?termId={g.TermId}";

        Assert.Equal(HttpStatusCode.OK, (await g.Teacher.Client.GetAsync(book)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await g.School.Owner.GetAsync(book)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync(book)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await g.Students[0].Client.GetAsync(book)).StatusCode);

        var assistant = await g.School.AddMemberAsync("Assistant", prefix: "ast");
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.Client.GetAsync(book)).StatusCode);

        // The assignment must belong to the class in the URL.
        var otherClass = await g.School.CreateClassAsync("7-B");
        Assert.Equal(HttpStatusCode.NotFound,
            (await g.Teacher.Client.GetAsync($"{g.School.Api}/classes/{otherClass}/assignments/{g.AssignmentId}/gradebook?termId={g.TermId}")).StatusCode);
    }

    [Fact]
    public async Task Grades_protect_the_subject_and_export_to_excel()
    {
        var g = await GradebookAsync();
        var category = await AddCategoryAsync(g, "Classwork", 100);
        var item = await AddItemAsync(g, category, "Sheet 1", 10);
        (await Scores(g, item, (g.Students[0].UserId, 9m))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await g.School.Owner.DeleteAsync($"{g.School.Api}/classes/{g.ClassId}/assignments/{g.AssignmentId}")).StatusCode);
        // Removing the term that holds them is refused too; the other term can go.
        var secondTermId = (await GetDataAsync(g.School.Owner, $"{g.School.Api}/academic-years"))[0].GetProperty("terms")[1].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await g.School.Owner.PutAsJsonAsync($"{g.School.Api}/academic-years/{g.YearId}",
            new { name = "1448", startDate = Day(-30), endDate = Day(300), terms = new[] { new { id = secondTermId, name = "Second semester", startDate = Day(101), endDate = Day(300) } } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await g.School.Owner.PutAsJsonAsync($"{g.School.Api}/academic-years/{g.YearId}",
            new { name = "1448", startDate = Day(-30), endDate = Day(300), terms = new[] { new { id = g.TermId, name = "Whole year", startDate = Day(-30), endDate = Day(300) } } })).StatusCode);

        var export = await g.Teacher.Client.GetAsync($"{g.Url}/export?termId={g.TermId}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet(1);
        Assert.Contains("Sheet 1", sheet.Cell(1, 3).GetString());
        var row = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == g.Students[0].UserName);
        Assert.Equal(9, row.Cell(3).GetDouble());

        // With no students and no grades left, the subject can go.
        (await g.Teacher.Client.DeleteAsync($"{g.Url}/items/{item}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await g.School.Owner.DeleteAsync($"{g.School.Api}/classes/{g.ClassId}/assignments/{g.AssignmentId}")).StatusCode);
    }

    [Fact]
    public async Task Custom_subject_publishes_and_linked_homework_flows_into_the_gradebook_unless_overridden()
    {
        var school = await TestSchool.CreateAsync(factory);
        var (year, termId, _) = await YearAsync(school);
        var curriculumId = (await GetDataAsync(school.Owner, "/api/Lookups/curricula?countryCode=SA")).EnumerateArray()
            .First(c => c.GetProperty("code").GetString() == "SA_NATIONAL").GetProperty("id").GetInt32();
        var stageId = (await GetDataAsync(school.Owner, $"/api/Lookups/education-stages?curriculumId={curriculumId}"))[1].GetProperty("id").GetInt32();
        var gradeId = (await GetDataAsync(school.Owner, $"/api/Lookups/education-grades?stageId={stageId}"))[0].GetProperty("id").GetInt32();
        var classId = (await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/classes",
            new { name = "7-R", kind = "Section", gender = "Mixed", curriculumId, gradeId }))).GetProperty("id").GetString()!;

        var teacher = await school.AddMemberAsync("Teacher", prefix: "rob");
        var student = await school.AddMemberAsync("Student", prefix: "robs");
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/students", new { userNames = new[] { student.UserName } })).EnsureSuccessStatusCode();

        // "Robotics" is the school's own subject, not in the lookup list.
        var assignment = await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments",
            new { subjectName = "Robotics", teacherUserId = teacher.UserId }));
        var assignmentId = assignment.GetProperty("id").GetString()!;
        var courseId = assignment.GetProperty("courseId").GetString()!;
        var org = school.Slug;

        var version = (await GetDraftAsync(teacher.Client, org, courseId)).GetProperty("curriculumVersion").GetInt32();
        var tree = await DataAsync(await SyncAsync(teacher.Client, org, courseId, version,
            [new { title = "Unit 1", lessons = new[] { Lesson("Intro"), Lesson("Build a robot", type: "Assignment", videoUrl: null) } }]));
        var videoLessonId = tree.GetProperty("chapters")[0].GetProperty("lessons")[0].GetProperty("id").GetString()!;
        var homeworkId = tree.GetProperty("chapters")[0].GetProperty("lessons")[1].GetProperty("id").GetString()!;
        (await teacher.Client.PutAsJsonAsync($"{school.Api}/courses/{courseId}/lessons/{homeworkId}/assignment",
            new { maxPoints = 20, acceptLateSubmissions = true })).EnsureSuccessStatusCode();

        // Publishing is the school's decision; a custom subject must not block it.
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PutAsJsonAsync($"{school.Api}/courses/{courseId}/status", new { status = "Published" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await school.Owner.PutAsJsonAsync($"{school.Api}/courses/{courseId}/status", new { status = "Published" })).StatusCode);

        var submission = await DataAsync(await student.Client.PutAsJsonAsync($"{school.Api}/learn/lessons/{homeworkId}/assignment/submission",
            new { text = "Done", links = Array.Empty<string>() }));
        (await teacher.Client.PutAsJsonAsync($"{school.Api}/submissions/{submission.GetProperty("id").GetString()}/grade",
            new { score = 15, returnForRevision = false })).EnsureSuccessStatusCode();

        var url = $"{school.Api}/classes/{classId}/assignments/{assignmentId}/gradebook";
        var categoryId = (await DataAsync(await teacher.Client.PostAsJsonAsync($"{url}/categories", new { termId, name = "Homework", weightPercent = 100 }))).GetProperty("id").GetString()!;

        // Only quiz and homework lessons of this course can be linked.
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.Client.PostAsJsonAsync($"{url}/categories/{categoryId}/items",
            new { title = "Video", maxPoints = 10, sourceLessonId = videoLessonId })).StatusCode);

        var item = await DataAsync(await teacher.Client.PostAsJsonAsync($"{url}/categories/{categoryId}/items",
            new { title = "Robot", maxPoints = 10, sourceLessonId = homeworkId }));
        Assert.Equal("Assignment", item.GetProperty("source").GetString());
        var itemId = item.GetProperty("id").GetString()!;

        // 15 of 20 on the homework is 7.5 of 10 here.
        var book = await GetDataAsync(teacher.Client, $"{url}?termId={termId}");
        Assert.Contains(book.GetProperty("linkableLessons").EnumerateArray(), l => l.GetProperty("lessonId").GetString() == homeworkId);
        var cell = Row(book, student.UserId).GetProperty("cells")[0];
        Assert.Equal(7.5m, cell.GetProperty("score").GetDecimal());
        Assert.True(cell.GetProperty("fromLesson").GetBoolean());

        // A typed score wins, and clearing it goes back to the lesson's score.
        (await teacher.Client.PutAsJsonAsync($"{url}/items/{itemId}/scores", new { scores = new[] { new { userId = student.UserId, score = (decimal?)9 } } })).EnsureSuccessStatusCode();
        var typed = Row(await GetDataAsync(teacher.Client, $"{url}?termId={termId}"), student.UserId).GetProperty("cells")[0];
        Assert.Equal(9m, typed.GetProperty("score").GetDecimal());
        Assert.False(typed.GetProperty("fromLesson").GetBoolean());

        (await teacher.Client.PutAsJsonAsync($"{url}/items/{itemId}/scores", new { scores = new[] { new { userId = student.UserId, score = (decimal?)null } } })).EnsureSuccessStatusCode();
        var back = Row(await GetDataAsync(teacher.Client, $"{url}?termId={termId}"), student.UserId).GetProperty("cells")[0];
        Assert.Equal(7.5m, back.GetProperty("score").GetDecimal());
        Assert.True(back.GetProperty("fromLesson").GetBoolean());
    }

    [Fact]
    public async Task A_subject_can_follow_another_curriculum_than_its_class()
    {
        var school = await TestSchool.CreateAsync(factory);
        var curricula = (await GetDataAsync(school.Owner, "/api/Lookups/curricula?countryCode=SA")).EnumerateArray().ToList();
        var national = curricula.First(c => c.GetProperty("code").GetString() == "SA_NATIONAL").GetProperty("id").GetInt32();
        var other = curricula.First(c => c.GetProperty("code").GetString() != "SA_NATIONAL").GetProperty("id").GetInt32();
        var stageId = (await GetDataAsync(school.Owner, $"/api/Lookups/education-stages?curriculumId={national}"))[1].GetProperty("id").GetInt32();
        var gradeId = (await GetDataAsync(school.Owner, $"/api/Lookups/education-grades?stageId={stageId}"))[0].GetProperty("id").GetInt32();
        var classId = (await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/classes",
            new { name = "9-I", kind = "Section", gender = "Mixed", curriculumId = national, gradeId }))).GetProperty("id").GetString()!;
        var teacher = await school.AddMemberAsync("Teacher", prefix: "mix");

        Assert.Equal(HttpStatusCode.BadRequest, (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments",
            new { subjectName = "Nope", teacherUserId = teacher.UserId, curriculumId = 999999 })).StatusCode);

        var assignment = await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments",
            new { subjectName = "Maths (IGCSE)", teacherUserId = teacher.UserId, curriculumId = other }));
        Assert.Equal(other, assignment.GetProperty("curriculumId").GetInt32());

        var draft = await GetDraftAsync(teacher.Client, school.Slug, assignment.GetProperty("courseId").GetString()!);
        Assert.Equal(other, draft.GetProperty("curriculumId").GetInt32());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("gradeId").ValueKind); // the class's grade belongs to the other curriculum
    }

    [Fact]
    public async Task Report_cards_are_private_until_published_and_only_reach_the_family()
    {
        var g = await GradebookAsync();
        var category = await AddCategoryAsync(g, "Classwork", 100);
        var item = await AddItemAsync(g, category, "Sheet 1", 10);
        var child = g.Students[0];
        (await Scores(g, item, (child.UserId, 9.5m), (g.Students[1].UserId, 5m))).EnsureSuccessStatusCode();

        (await g.Teacher.Client.PutAsJsonAsync($"{g.School.Api}/classes/{g.ClassId}/attendance", new
        {
            date = Today,
            marks = new[] { new { userId = child.UserId, status = "Absent" } }
        })).EnsureSuccessStatusCode();

        var parent = await FamilyTestHelpers.ParentAsync(factory);
        (await g.School.Owner.PostAsJsonAsync($"{g.School.Api}/members/{child.UserId}/parents", new { parentUserName = parent.UserName })).EnsureSuccessStatusCode();
        var strangerParent = await FamilyTestHelpers.ParentAsync(factory, "str");

        var cards = $"{g.School.Api}/report-cards/students/{child.UserId}";
        var family = $"{cards}/terms/{g.TermId}";

        var staffView = await GetDataAsync(g.Teacher.Client, $"{family}/staff");
        Assert.False(staffView.GetProperty("published").GetBoolean());
        Assert.Equal(95m, staffView.GetProperty("overallPercent").GetDecimal());
        Assert.Equal("Excellent", staffView.GetProperty("overallLabel").GetString());
        var subject = Assert.Single(staffView.GetProperty("subjects").EnumerateArray());
        Assert.Equal("Science", subject.GetProperty("subjectName").GetString());
        Assert.Equal(1, staffView.GetProperty("attendance").GetProperty("absent").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await child.Client.GetAsync(family)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.Client.GetAsync(family)).StatusCode);
        Assert.Equal(0, (await GetDataAsync(parent.Client, $"{cards}/terms")).GetArrayLength());

        Assert.Equal(HttpStatusCode.Forbidden, (await g.Teacher.Client.PutAsJsonAsync($"{g.School.Api}/academic-years/terms/{g.TermId}/report-cards", new { published = true })).StatusCode);
        (await g.School.Owner.PutAsJsonAsync($"{g.School.Api}/academic-years/terms/{g.TermId}/report-cards", new { published = true })).EnsureSuccessStatusCode();

        Assert.Equal(95m, (await GetDataAsync(child.Client, family)).GetProperty("overallPercent").GetDecimal());
        Assert.Equal(95m, (await GetDataAsync(parent.Client, family)).GetProperty("overallPercent").GetDecimal());
        Assert.Equal(1, (await GetDataAsync(parent.Client, $"{cards}/terms")).GetArrayLength());

        // Other families and classmates stay out.
        Assert.Equal(HttpStatusCode.NotFound, (await strangerParent.Client.GetAsync(family)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await g.Students[1].Client.GetAsync(family)).StatusCode);

        // And another school's staff cannot reach this student's card.
        var otherSchool = await TestSchool.CreateAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await otherSchool.Owner.GetAsync($"{otherSchool.Api}/report-cards/students/{child.UserId}/terms/{g.TermId}/staff")).StatusCode);

        // Unpublishing hides it again.
        (await g.School.Owner.PutAsJsonAsync($"{g.School.Api}/academic-years/terms/{g.TermId}/report-cards", new { published = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await parent.Client.GetAsync(family)).StatusCode);
    }

    [Fact]
    public async Task The_organization_scale_decides_the_label()
    {
        var g = await GradebookAsync();
        var category = await AddCategoryAsync(g, "Classwork", 100);
        var item = await AddItemAsync(g, category, "Sheet 1", 10);
        (await Scores(g, item, (g.Students[0].UserId, 7m))).EnsureSuccessStatusCode();

        var settings = await GetDataAsync(g.School.Owner, $"{g.School.Api}/settings");
        Assert.Equal("Descriptive", settings.GetProperty("gradingScale").GetString());

        async Task<JsonElement> Label(string scale)
        {
            (await g.School.Owner.PutAsJsonAsync($"{g.School.Api}/settings", new
            {
                name = settings.GetProperty("name").GetString(),
                timeZone = settings.GetProperty("timeZone").GetString(),
                calendar = settings.GetProperty("calendar").GetString(),
                weekStartDay = settings.GetProperty("weekStartDay").GetString(),
                gradingScale = scale
            })).EnsureSuccessStatusCode();
            return Row(await GetDataAsync(g.Teacher.Client, $"{g.Url}?termId={g.TermId}"), g.Students[0].UserId).GetProperty("label");
        }

        Assert.Equal("C", (await Label("Letters")).GetString());
        Assert.Equal(JsonValueKind.Null, (await Label("Percent")).ValueKind);
        Assert.Equal("Good", (await Label("Descriptive")).GetString());
    }

    [Fact]
    public async Task Attendance_exports_students_by_date()
    {
        var g = await GradebookAsync();
        var url = $"{g.School.Api}/classes/{g.ClassId}/attendance";
        (await g.Teacher.Client.PutAsJsonAsync(url, new
        {
            date = Today,
            marks = new[] { new { userId = g.Students[0].UserId, status = "Absent" }, new { userId = g.Students[1].UserId, status = "Present" } }
        })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await g.Teacher.Client.GetAsync($"{url}/export?from={Day(1)}&to={Day(-1)}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await g.Students[0].Client.GetAsync($"{url}/export?from={Today}&to={Today}")).StatusCode);

        var export = await g.Teacher.Client.GetAsync($"{url}/export?from={Today}&to={Today}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet(1);
        Assert.Equal(Today, sheet.Cell(1, 3).GetString());
        var row = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == g.Students[0].UserName);
        Assert.Equal("غائب", row.Cell(3).GetString());
        Assert.Equal(1, row.Cell(5).GetDouble()); // the absent total
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private sealed record Gradebook(
        TestSchool School, string YearId, string TermId, string ClassId, string AssignmentId,
        (HttpClient Client, string UserName, string UserId) Teacher,
        List<(HttpClient Client, string UserName, string UserId)> Students)
    {
        public string Url => $"{School.Api}/classes/{ClassId}/assignments/{AssignmentId}/gradebook";
    }

    private static async Task<(string YearId, string TermId, string SecondTermId)> YearAsync(TestSchool school)
    {
        var year = await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/academic-years", YearBody()));
        return (year.GetProperty("id").GetString()!, year.GetProperty("terms")[0].GetProperty("id").GetString()!, year.GetProperty("terms")[1].GetProperty("id").GetString()!);
    }

    private async Task<Gradebook> GradebookAsync()
    {
        var school = await TestSchool.CreateAsync(factory);
        var (yearId, termId, _) = await YearAsync(school);
        var classId = await school.CreateClassAsync("7-A");
        var teacher = await school.AddMemberAsync("Teacher", prefix: "gbt");
        var assignment = await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments", new { subjectName = "Science", teacherUserId = teacher.UserId }));

        var students = new List<(HttpClient Client, string UserName, string UserId)>();
        for (var i = 0; i < 3; i++)
            students.Add(await school.AddMemberAsync("Student", prefix: $"gb{i}"));
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/students", new { userNames = students.Select(s => s.UserName).ToArray() })).EnsureSuccessStatusCode();

        return new Gradebook(school, yearId, termId, classId, assignment.GetProperty("id").GetString()!, teacher, students);
    }

    private static async Task<string> AddCategoryAsync(Gradebook g, string name, decimal weightPercent) =>
        (await DataAsync(await g.Teacher.Client.PostAsJsonAsync($"{g.Url}/categories", new { termId = g.TermId, name, weightPercent }))).GetProperty("id").GetString()!;

    private static async Task<string> AddItemAsync(Gradebook g, string categoryId, string title, decimal maxPoints) =>
        (await DataAsync(await g.Teacher.Client.PostAsJsonAsync($"{g.Url}/categories/{categoryId}/items", new { title, maxPoints }))).GetProperty("id").GetString()!;

    private static Task<HttpResponseMessage> Scores(Gradebook g, string itemId, params (string UserId, decimal? Score)[] scores) =>
        g.Teacher.Client.PutAsJsonAsync($"{g.Url}/items/{itemId}/scores", new { scores = scores.Select(s => new { userId = s.UserId, score = s.Score }).ToArray() });

    private static JsonElement Row(JsonElement book, string userId) =>
        book.GetProperty("students").EnumerateArray().Single(r => r.GetProperty("userId").GetString() == userId);
}
