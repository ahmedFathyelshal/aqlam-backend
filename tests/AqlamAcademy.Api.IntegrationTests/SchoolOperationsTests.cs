using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static AqlamAcademy.Api.IntegrationTests.TestSchool;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SchoolOperationsTests(ApiFactory factory)
{
    private static readonly string Today = DateTime.UtcNow.ToString("yyyy-MM-dd"); // Riyadh is ahead of UTC, so UTC today is never "future"

    [Fact]
    public async Task Attendance_sheet_is_an_idempotent_upsert_with_a_summary()
    {
        var (school, classId, teacher, students) = await ClassWithStudentsAsync(2);
        var url = $"{school.Api}/classes/{classId}/attendance";

        var empty = await GetDataAsync(teacher.Client, $"{url}?date={Today}");
        Assert.False(empty.GetProperty("isTaken").GetBoolean());
        Assert.All(empty.GetProperty("lines").EnumerateArray(), l => Assert.Equal(JsonValueKind.Null, l.GetProperty("status").ValueKind));

        object Sheet(string first, string second) => new
        {
            date = Today,
            marks = new[] { new { userId = students[0].UserId, status = first, note = (string?)null }, new { userId = students[1].UserId, status = second, note = (string?)"Bus" } }
        };
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync(url, Sheet("Present", "Late"))).StatusCode);
        var resent = await DataAsync(await teacher.Client.PutAsJsonAsync(url, Sheet("Present", "Absent"))); // offline phone resends, edited
        Assert.True(resent.GetProperty("isTaken").GetBoolean());
        Assert.Equal("Absent", resent.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("userId").GetString() == students[1].UserId).GetProperty("status").GetString());

        var summary = await GetDataAsync(teacher.Client, $"{url}/summary?from={Today}&to={Today}");
        var second = summary.EnumerateArray().Single(r => r.GetProperty("userId").GetString() == students[1].UserId);
        Assert.Equal(1, second.GetProperty("absent").GetInt32());
        Assert.Equal(0, second.GetProperty("late").GetInt32()); // one sheet, one record per student

        var tomorrow = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.Client.PutAsJsonAsync(url, new { date = tomorrow, marks = new[] { new { userId = students[0].UserId, status = "Present" } } })).StatusCode);
        var outsider = await school.AddMemberAsync("Student", prefix: "out");
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.Client.PutAsJsonAsync(url, new { date = Today, marks = new[] { new { userId = outsider.UserId, status = "Present" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await students[0].Client.GetAsync($"{url}?date={Today}")).StatusCode);
    }

    [Fact]
    public async Task Center_groups_take_attendance_per_meeting()
    {
        var (school, classId, teacher, students) = await ClassWithStudentsAsync(1, type: "Center");
        var url = $"{school.Api}/classes/{classId}/attendance";
        var mark = new[] { new { userId = students[0].UserId, status = "Present" } };

        (await teacher.Client.PutAsJsonAsync(url, new { date = Today, startTime = "09:00", marks = mark })).EnsureSuccessStatusCode();
        (await teacher.Client.PutAsJsonAsync(url, new { date = Today, startTime = "17:00", marks = mark })).EnsureSuccessStatusCode();

        var summary = await GetDataAsync(teacher.Client, $"{url}/summary?from={Today}&to={Today}");
        Assert.Equal(2, summary[0].GetProperty("present").GetInt32());
        Assert.True((await GetDataAsync(teacher.Client, $"{url}?date={Today}&startTime=17:00")).GetProperty("isTaken").GetBoolean());
        Assert.False((await GetDataAsync(teacher.Client, $"{url}?date={Today}")).GetProperty("isTaken").GetBoolean());
    }

    [Fact]
    public async Task Parents_link_by_code_see_a_read_only_overview_and_get_absence_alerts_once()
    {
        var (school, classId, teacher, students) = await ClassWithStudentsAsync(1);
        var child = students[0];
        var parent = await FamilyTestHelpers.ParentAsync(factory);

        // Before linking the parent cannot see anything in the school.
        Assert.Equal(HttpStatusCode.NotFound, (await parent.Client.GetAsync($"{school.Api}/children/{child.UserId}")).StatusCode);

        var invite = await DataAsync(await school.Owner.PostAsync($"{school.Api}/members/{child.UserId}/parent-invites", null));
        var code = invite.GetProperty("code").GetString()!;
        var linked = await DataAsync(await parent.Client.PostAsJsonAsync("/api/me/children", new { code = code.ToLowerInvariant() }));
        Assert.Equal(school.Slug, linked.GetProperty("organizations")[0].GetProperty("slug").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await parent.Client.PostAsJsonAsync("/api/me/children", new { code })).StatusCode); // one-time

        // Absence alert: once per change, again when it becomes Late.
        var url = $"{school.Api}/classes/{classId}/attendance";
        object Sheet(string status) => new { date = Today, marks = new[] { new { userId = child.UserId, status } } };
        (await teacher.Client.PutAsJsonAsync(url, Sheet("Absent"))).EnsureSuccessStatusCode();
        (await teacher.Client.PutAsJsonAsync(url, Sheet("Absent"))).EnsureSuccessStatusCode();
        var inbox = await GetDataAsync(parent.Client, "/api/me/notifications");
        Assert.Equal(1, inbox.GetProperty("unreadCount").GetInt32());
        Assert.Equal("Absence", inbox.GetProperty("items")[0].GetProperty("type").GetString());
        Assert.Contains(child.UserId, inbox.GetProperty("items")[0].GetProperty("link").GetString());
        (await teacher.Client.PutAsJsonAsync(url, Sheet("Late"))).EnsureSuccessStatusCode();
        Assert.Equal(2, (await GetDataAsync(parent.Client, "/api/me/notifications")).GetProperty("unreadCount").GetInt32());

        var overview = await GetDataAsync(parent.Client, $"{school.Api}/children/{child.UserId}");
        Assert.Equal("7-A", overview.GetProperty("classes")[0].GetString());
        Assert.Equal(1, overview.GetProperty("attendance").GetProperty("late").GetInt32());

        // Read-only: the Parent role grants nothing else.
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.Client.GetAsync($"{school.Api}/classes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.Client.GetAsync($"{url}?date={Today}")).StatusCode);

        // Another student's overview is not visible to this parent.
        var other = await school.AddMemberAsync("Student", prefix: "oth");
        Assert.Equal(HttpStatusCode.NotFound, (await parent.Client.GetAsync($"{school.Api}/children/{other.UserId}")).StatusCode);

        (await parent.Client.PostAsync("/api/me/notifications/read-all", null)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await GetDataAsync(parent.Client, "/api/me/notifications")).GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task A_child_has_at_most_two_parents_and_unlinking_ends_access()
    {
        var school = await TestSchool.CreateAsync(factory);
        var child = await school.AddMemberAsync("Student", prefix: "kid");
        var mother = await FamilyTestHelpers.ParentAsync(factory, "mom");
        var father = await FamilyTestHelpers.ParentAsync(factory, "dad");
        var third = await FamilyTestHelpers.ParentAsync(factory, "thr");
        var parentsUrl = $"{school.Api}/members/{child.UserId}/parents";

        Assert.Equal(HttpStatusCode.OK, (await school.Owner.PostAsJsonAsync(parentsUrl, new { parentUserName = mother.UserName })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.PostAsJsonAsync(parentsUrl, new { parentUserName = mother.UserName })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await school.Owner.PostAsJsonAsync(parentsUrl, new { parentUserName = father.UserName })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.PostAsJsonAsync(parentsUrl, new { parentUserName = third.UserName })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.PostAsync($"{school.Api}/members/{child.UserId}/parent-invites", null)).StatusCode);

        Assert.Equal(2, (await GetDataAsync(school.Owner, parentsUrl)).GetArrayLength());
        var members = await GetDataAsync(school.Owner, $"{school.Api}/members");
        Assert.Contains("Parent", members.EnumerateArray().Single(m => m.GetProperty("userName").GetString() == mother.UserName).GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        var children = await GetDataAsync(mother.Client, "/api/me/children");
        Assert.Equal(child.UserId, children[0].GetProperty("userId").GetString());
        Assert.Equal(HttpStatusCode.OK, (await mother.Client.GetAsync($"{school.Api}/children/{child.UserId}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await school.Owner.DeleteAsync($"{parentsUrl}/{mother.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mother.Client.GetAsync($"{school.Api}/children/{child.UserId}")).StatusCode);
        Assert.Empty((await GetDataAsync(mother.Client, "/api/me/children")).EnumerateArray());
    }

    [Fact]
    public async Task Announcements_reach_the_chosen_audience_and_teachers_are_limited_to_their_classes()
    {
        var (school, classId, teacher, students) = await ClassWithStudentsAsync(2);
        var parent = await FamilyTestHelpers.ParentAsync(factory);
        (await school.Owner.PostAsJsonAsync($"{school.Api}/members/{students[0].UserId}/parents", new { parentUserName = parent.UserName })).EnsureSuccessStatusCode();
        var otherClass = await school.CreateClassAsync("7-B");

        var toClass = await teacher.Client.PostAsJsonAsync($"{school.Api}/announcements", new
        {
            title = "Science fair",
            body = "Bring your projects on Sunday.",
            scope = "Class",
            classGroupId = classId,
            audience = new[] { "Students", "Parents" },
            sendEmail = true
        });
        Assert.Equal(HttpStatusCode.OK, toClass.StatusCode);
        Assert.Equal(3, (await DataAsync(toClass)).GetProperty("recipientCount").GetInt32()); // 2 students + 1 parent

        var studentInbox = await GetDataAsync(students[1].Client, "/api/me/notifications");
        Assert.Equal("Science fair", studentInbox.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal("Al Rowad School", studentInbox.GetProperty("items")[0].GetProperty("organizationName").GetString());
        Assert.Equal(1, (await GetDataAsync(parent.Client, "/api/me/notifications")).GetProperty("unreadCount").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync($"{school.Api}/announcements",
            new { title = "x", body = "y", scope = "Class", classGroupId = otherClass, audience = new[] { "Students" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync($"{school.Api}/announcements",
            new { title = "x", body = "y", scope = "Organization", audience = new[] { "Students" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await students[0].Client.PostAsJsonAsync($"{school.Api}/announcements",
            new { title = "x", body = "y", scope = "Class", classGroupId = classId, audience = new[] { "Students" } })).StatusCode);

        var staffOnly = await DataAsync(await school.Owner.PostAsJsonAsync($"{school.Api}/announcements",
            new { title = "Staff meeting", body = "Thursday 1pm.", scope = "Organization", audience = new[] { "Staff" } }));
        Assert.Equal(1, staffOnly.GetProperty("recipientCount").GetInt32()); // the teacher (the owner is the author)
        Assert.DoesNotContain((await GetDataAsync(students[0].Client, "/api/me/notifications")).GetProperty("items").EnumerateArray(),
            n => n.GetProperty("title").GetString() == "Staff meeting");

        var list = await GetDataAsync(teacher.Client, $"{school.Api}/announcements");
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal(new[] { "Students", "Parents" }, list[1].GetProperty("audience").EnumerateArray().Select(a => a.GetString()));

        var studentNotification = (await GetDataAsync(students[1].Client, "/api/me/notifications")).GetProperty("items")[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await parent.Client.PostAsync($"/api/me/notifications/{studentNotification}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await students[1].Client.PostAsync($"/api/me/notifications/{studentNotification}/read", null)).StatusCode);
    }

    [Fact]
    public async Task Organization_settings_control_calendar_and_week_start()
    {
        var school = await TestSchool.CreateAsync(factory);
        var settings = await GetDataAsync(school.Owner, $"{school.Api}/settings");
        Assert.Equal("Both", settings.GetProperty("calendar").GetString()); // Saudi default
        Assert.Equal("Sunday", settings.GetProperty("weekStartDay").GetString());

        var bad = await school.Owner.PutAsJsonAsync($"{school.Api}/settings", new { name = "X", timeZone = "Mars/Olympus", calendar = "Hijri", weekStartDay = "Sunday" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var updated = await DataAsync(await school.Owner.PutAsJsonAsync($"{school.Api}/settings",
            new { name = "Rowad International", timeZone = "Asia/Riyadh", calendar = "Hijri", weekStartDay = "Sunday", primaryColor = "#1e6fd9" }));
        Assert.Equal("#1E6FD9", updated.GetProperty("primaryColor").GetString());

        var membership = (await GetDataAsync(school.Owner, "/api/me/memberships")).EnumerateArray().Single(m => m.GetProperty("slug").GetString() == school.Slug);
        Assert.Equal("Hijri", membership.GetProperty("calendar").GetString());
        Assert.Equal("Rowad International", membership.GetProperty("name").GetString());

        var teacher = await school.AddMemberAsync("Teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync($"{school.Api}/settings")).StatusCode);
    }

    private async Task<(TestSchool School, string ClassId, (HttpClient Client, string UserName, string UserId) Teacher, List<(HttpClient Client, string UserName, string UserId)> Students)>
        ClassWithStudentsAsync(int studentCount, string type = "School")
    {
        var school = await TestSchool.CreateAsync(factory, type);
        var classId = await school.CreateClassAsync("7-A", kind: type == "School" ? "Section" : "Group");
        var teacher = await school.AddMemberAsync("Teacher", prefix: "hrt");
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments", new { subjectName = "Science", teacherUserId = teacher.UserId })).EnsureSuccessStatusCode();

        var students = new List<(HttpClient, string, string)>();
        for (var i = 0; i < studentCount; i++)
            students.Add(await school.AddMemberAsync("Student", prefix: $"s{i}"));
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/students", new { userNames = students.Select(s => s.Item2).ToArray() })).EnsureSuccessStatusCode();

        return (school, classId, teacher, students);
    }
}
