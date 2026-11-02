using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static AqlamAcademy.Api.IntegrationTests.TestSchool;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ClassesTests(ApiFactory factory)
{
    [Fact]
    public async Task Gender_rules_follow_the_campus_and_the_roster()
    {
        var school = await TestSchool.CreateAsync(factory);
        var branch = await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/branches", new { name = "Girls campus", gender = "Girls" }));
        var branchId = branch.GetProperty("id").GetString();

        var boysInGirlsCampus = await school.Owner.PostAsJsonAsync($"{school.Api}/classes", new { name = "7-A", kind = "Section", gender = "Boys", branchId });
        Assert.Equal(HttpStatusCode.BadRequest, boysInGirlsCampus.StatusCode);

        var classId = await school.CreateClassAsync("7-B", gender: "Girls", branchId: branchId, capacity: 3);
        var girl = await school.AddMemberAsync("Student", "Female", "girl");
        var boy = await school.AddMemberAsync("Student", "Male", "boy");
        var unknown = await school.AddMemberAsync("Student", prefix: "unk");
        var teacher = await school.AddMemberAsync("Teacher", prefix: "tch");

        var results = await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/students",
            new { userNames = new[] { girl.UserName, boy.UserName, unknown.UserName, teacher.UserName, "nobody-here", girl.UserName } }));

        Assert.Equal(5, results.GetArrayLength()); // duplicates collapsed
        Assert.Equal("Added", StatusOf(results, girl.UserName));
        Assert.Equal("Failed", StatusOf(results, boy.UserName));
        Assert.Equal("Added", StatusOf(results, unknown.UserName));   // unknown gender is allowed
        Assert.Equal("Failed", StatusOf(results, teacher.UserName));  // only students
        Assert.Equal("Failed", StatusOf(results, "nobody-here"));

        // The branch cannot become a boys' campus while it hosts a girls' class; the class cannot flip either.
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.PutAsJsonAsync($"{school.Api}/branches/{branchId}", new { name = "Girls campus", gender = "Boys" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await school.Owner.PutAsJsonAsync($"{school.Api}/classes/{classId}",
            new { name = "7-B", kind = "Section", gender = "Mixed", branchId, capacity = 1 })).StatusCode); // capacity below roster size

        // Capacity: 2 on the roster, limit 3.
        var more = await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/students",
            new { userNames = new[] { (await school.AddMemberAsync("Student", "Female", "g2")).UserName, (await school.AddMemberAsync("Student", "Female", "g3")).UserName } }));
        Assert.Equal("Added", more[0].GetProperty("status").GetString());
        Assert.Equal("The class is full.", more[1].GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.DeleteAsync($"{school.Api}/branches/{branchId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.DeleteAsync($"{school.Api}/classes/{classId}")).StatusCode);
    }

    [Fact]
    public async Task Teacher_assignment_creates_the_subject_course_and_rosters_follow_publishing()
    {
        var school = await TestSchool.CreateAsync(factory);
        var classId = await school.CreateClassAsync("8-A");
        var teacher = await school.AddMemberAsync("Teacher", prefix: "math");
        var early = await school.AddMemberAsync("Student", prefix: "early");
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/students", new { userNames = new[] { early.UserName } })).EnsureSuccessStatusCode();

        var subjectId = (await GetDataAsync(school.Owner, "/api/Lookups/subjects"))[0].GetProperty("id").GetInt32();
        var assign = await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments", new { subjectId, teacherUserId = teacher.UserId });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var assignment = await DataAsync(assign);
        var courseId = assignment.GetProperty("courseId").GetString()!;
        Assert.Equal("Draft", assignment.GetProperty("courseStatus").GetString());

        // Same subject twice → conflict; a non-teacher cannot be assigned.
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments", new { subjectId, teacherUserId = teacher.UserId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments",
            new { subjectName = "Art", teacherUserId = early.UserId })).StatusCode);

        // The teacher finds the class and its course; the draft course is not given to students yet.
        var mine = await GetDataAsync(teacher.Client, $"{school.Api}/classes?mine=true");
        Assert.Equal(classId, mine[0].GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.GetAsync($"{school.Api}/courses/{courseId}/draft")).StatusCode);
        Assert.DoesNotContain((await GetDataAsync(early.Client, "/api/me/courses")).EnumerateArray(), c => c.GetProperty("courseId").GetString() == courseId);

        // Publishing gives the whole roster access; later students get it on joining.
        await CourseTestHelpers.MakePublishableAsync(school.Owner, school.Slug, courseId);
        (await school.Owner.PutAsJsonAsync($"{school.Api}/courses/{courseId}/status", new { status = "Published" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await early.Client.GetAsync($"{school.Api}/learn/courses/{courseId}")).StatusCode);

        var late = await school.AddMemberAsync("Student", prefix: "late");
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/students", new { userNames = new[] { late.UserName } })).EnsureSuccessStatusCode();
        var lateCourses = await GetDataAsync(late.Client, "/api/me/courses");
        Assert.Contains(lateCourses.EnumerateArray(), c => c.GetProperty("courseId").GetString() == courseId);

        // Leaving the class removes class-given access.
        Assert.Equal(HttpStatusCode.OK, (await school.Owner.DeleteAsync($"{school.Api}/classes/{classId}/students/{late.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await late.Client.GetAsync($"{school.Api}/learn/courses/{courseId}")).StatusCode);

        // Removing the subject from the class revokes it for the rest; the course itself is kept.
        var assignmentId = assignment.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await school.Owner.DeleteAsync($"{school.Api}/classes/{classId}/assignments/{assignmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await early.Client.GetAsync($"{school.Api}/learn/courses/{courseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await school.Owner.GetAsync($"{school.Api}/courses/{courseId}/draft")).StatusCode);
    }

    [Fact]
    public async Task Access_given_another_way_survives_leaving_the_class_and_teachers_can_be_changed()
    {
        var school = await TestSchool.CreateAsync(factory);
        var classId = await school.CreateClassAsync("9-A");
        var first = await school.AddMemberAsync("Teacher", prefix: "t1");
        var second = await school.AddMemberAsync("Teacher", prefix: "t2");
        var student = await school.AddMemberAsync("Student", prefix: "keep");

        var assignment = await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments",
            new { subjectName = "Quran", teacherUserId = first.UserId }));
        var courseId = assignment.GetProperty("courseId").GetString()!;
        await CourseTestHelpers.MakePublishableAsync(school.Owner, school.Slug, courseId);
        (await school.Owner.PutAsJsonAsync($"{school.Api}/courses/{courseId}/status", new { status = "Published" })).EnsureSuccessStatusCode();

        // Enrolled by staff first (manual), then through the class: leaving the class keeps the manual access.
        (await school.Owner.PostAsJsonAsync($"{school.Api}/courses/{courseId}/enrollments", new { userNames = new[] { student.UserName } })).EnsureSuccessStatusCode();
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/students", new { userNames = new[] { student.UserName } })).EnsureSuccessStatusCode();
        (await school.Owner.DeleteAsync($"{school.Api}/classes/{classId}/students/{student.UserId}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await student.Client.GetAsync($"{school.Api}/learn/courses/{courseId}")).StatusCode);

        var changed = await DataAsync(await school.Owner.PutAsJsonAsync($"{school.Api}/classes/{classId}/assignments/{assignment.GetProperty("id").GetString()}",
            new { teacherUserId = second.UserId }));
        Assert.Equal(courseId, changed.GetProperty("courseId").GetString());
        Assert.Equal(second.UserName, changed.GetProperty("teacherUserName").GetString());
        Assert.Empty((await GetDataAsync(first.Client, $"{school.Api}/classes?mine=true")).EnumerateArray());
        Assert.Single((await GetDataAsync(second.Client, $"{school.Api}/classes?mine=true")).EnumerateArray());
    }

    [Fact]
    public async Task Only_class_managers_change_classes_and_other_organizations_cannot_see_them()
    {
        var school = await TestSchool.CreateAsync(factory);
        var classId = await school.CreateClassAsync("10-A");
        var teacher = await school.AddMemberAsync("Teacher", prefix: "plain");
        var student = await school.AddMemberAsync("Student", prefix: "stu");

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.GetAsync($"{school.Api}/classes/{classId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync($"{school.Api}/classes", new { name = "X", kind = "Section", gender = "Mixed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.Client.GetAsync($"{school.Api}/classes")).StatusCode);

        var other = await TestSchool.CreateAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Owner.GetAsync($"{other.Api}/classes/{classId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Owner.GetAsync($"{school.Api}/classes/{classId}")).StatusCode); // not a member
        Assert.Empty((await GetDataAsync(other.Owner, $"{other.Api}/classes")).EnumerateArray());
    }

    [Fact]
    public async Task Center_groups_keep_a_weekly_schedule()
    {
        var center = await TestSchool.CreateAsync(factory, type: "Center");
        var response = await center.Owner.PostAsJsonAsync($"{center.Api}/classes", new
        {
            name = "Physics — Sat/Mon 5pm",
            kind = "Group",
            gender = "Mixed",
            schedule = new[]
            {
                new { day = "Monday", startTime = "17:00", endTime = "18:30" },
                new { day = "Saturday", startTime = "17:00", endTime = "18:30" }
            }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = await DataAsync(response);
        Assert.Equal("Group", group.GetProperty("kind").GetString());
        Assert.Equal("Monday", group.GetProperty("schedule")[0].GetProperty("day").GetString());

        var invalid = await center.Owner.PostAsJsonAsync($"{center.Api}/classes", new
        {
            name = "Bad",
            kind = "Group",
            gender = "Mixed",
            schedule = new[] { new { day = "Sunday", startTime = "18:00", endTime = "17:00" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static string? StatusOf(JsonElement results, string userName) =>
        results.EnumerateArray().First(r => string.Equals(r.GetProperty("userName").GetString(), userName, StringComparison.OrdinalIgnoreCase))
            .GetProperty("status").GetString();
}
