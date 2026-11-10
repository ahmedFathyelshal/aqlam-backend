using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqlamAcademy.Application.Features.Organizations.Overview;
using static AqlamAcademy.Api.IntegrationTests.TestSchool;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OverviewTests(ApiFactory factory)
{
    /// <summary>The school's own date: the overview works in Asia/Riyadh, not in UTC.</summary>
    private static DateOnly RiyadhToday =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh")));

    [Fact]
    public async Task Owner_sees_today_classes_and_students_over_the_school_limits()
    {
        var school = await TestSchool.CreateAsync(factory);
        var classId = await school.CreateClassAsync("7-A");
        var other = await school.CreateClassAsync("7-B");
        var teacher = await school.AddMemberAsync("Teacher", prefix: "ovt");
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/assignments", new { subjectName = "Science", teacherUserId = teacher.UserId })).EnsureSuccessStatusCode();
        var first = await school.AddMemberAsync("Student", prefix: "ov1");
        var second = await school.AddMemberAsync("Student", prefix: "ov2");
        (await school.Owner.PostAsJsonAsync($"{school.Api}/classes/{classId}/students", new { userNames = new[] { first.UserName, second.UserName } })).EnsureSuccessStatusCode();

        var url = $"{school.Api}/classes/{classId}/attendance";
        async Task Mark(DateOnly date, string firstStatus) =>
            (await teacher.Client.PutAsJsonAsync(url, new
            {
                date = date.ToString("yyyy-MM-dd"),
                marks = new[] { new { userId = first.UserId, status = firstStatus }, new { userId = second.UserId, status = "Present" } }
            })).EnsureSuccessStatusCode();

        var today = RiyadhToday;
        for (var back = 1; back <= 3; back++)
            await Mark(today.AddDays(-back), "Absent"); // 3 absences = the default follow-up limit
        await Mark(today, "Late");

        var overview = await GetDataAsync(school.Owner, $"{school.Api}/overview");

        var todayBlock = overview.GetProperty("today");
        Assert.Equal(today.ToString("yyyy-MM-dd"), todayBlock.GetProperty("date").GetString());
        Assert.Equal(1, todayBlock.GetProperty("present").GetInt32());
        Assert.Equal(1, todayBlock.GetProperty("late").GetInt32());
        Assert.Equal(100, todayBlock.GetProperty("ratePercent").GetInt32()); // late still attended
        Assert.Equal(50, todayBlock.GetProperty("usualRatePercent").GetInt32());

        var classes = overview.GetProperty("classes").EnumerateArray().ToList();
        var taken = classes.Single(c => c.GetProperty("id").GetString() == classId);
        Assert.Equal("Taken", taken.GetProperty("today").GetString());
        Assert.Equal(2, taken.GetProperty("students").GetInt32());
        Assert.Equal(1, taken.GetProperty("followUp").GetInt32());
        Assert.NotEqual("Taken", classes.Single(c => c.GetProperty("id").GetString() == other).GetProperty("today").GetString());

        Assert.Equal(1, overview.GetProperty("followUpTotal").GetInt32());
        var student = overview.GetProperty("followUp")[0];
        Assert.Equal(first.UserId, student.GetProperty("userId").GetString());
        Assert.Equal(3, student.GetProperty("absent").GetInt32());
        Assert.Equal("7-A", student.GetProperty("className").GetString());

        Assert.Equal(4, overview.GetProperty("days").GetArrayLength());
        Assert.Equal(2, overview.GetProperty("school").GetProperty("students").GetInt32());
        Assert.Equal(3, overview.GetProperty("school").GetProperty("rules").GetProperty("followUpAbsentDays").GetInt32());

        // Teachers keep their own home; the overview is for people who run the school.
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync($"{school.Api}/overview")).StatusCode);

        // A teacher's day covers only the classes they teach.
        var myDay = await GetDataAsync(teacher.Client, $"{school.Api}/my-day");
        Assert.Equal(classId, myDay.GetProperty("classes").EnumerateArray().Single().GetProperty("id").GetString());
        Assert.Equal(2, myDay.GetProperty("school").GetProperty("students").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await first.Client.GetAsync($"{school.Api}/my-day")).StatusCode);
    }

    [Fact]
    public async Task A_new_school_gets_an_empty_overview_not_an_error()
    {
        var school = await TestSchool.CreateAsync(factory);
        var overview = await GetDataAsync(school.Owner, $"{school.Api}/overview");
        Assert.Equal("10:00:00", overview.GetProperty("school").GetProperty("rules").GetProperty("attendanceDueBy").GetString());
        Assert.Equal(30, overview.GetProperty("school").GetProperty("rules").GetProperty("groupAttendanceGraceMinutes").GetInt32());
        Assert.Equal(0, overview.GetProperty("classes").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("today").GetProperty("ratePercent").ValueKind);
        Assert.Equal(0, overview.GetProperty("grading").GetArrayLength());
    }

    [Fact]
    public async Task Each_school_sets_when_attendance_counts_as_late()
    {
        var school = await TestSchool.CreateAsync(factory);
        var url = $"{school.Api}/settings";
        object Body(string dueBy, int grace) => new { name = "Al Rowad", timeZone = "Asia/Riyadh", calendar = "Both", weekStartDay = "Sunday", attendanceDueBy = dueBy, groupAttendanceGraceMinutes = grace };

        var saved = await DataAsync(await school.Owner.PutAsJsonAsync(url, Body("07:30", 15)));
        Assert.Equal("07:30:00", saved.GetProperty("attendanceDueBy").GetString());
        Assert.Equal(15, saved.GetProperty("groupAttendanceGraceMinutes").GetInt32());

        var rules = (await GetDataAsync(school.Owner, $"{school.Api}/overview")).GetProperty("school").GetProperty("rules");
        Assert.Equal("07:30:00", rules.GetProperty("attendanceDueBy").GetString());
        Assert.Equal(15, rules.GetProperty("groupAttendanceGraceMinutes").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await school.Owner.PutAsJsonAsync(url, Body("07:30", 500))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await school.Owner.PutAsJsonAsync(url, Body("02:00", 15))).StatusCode);
    }

    [Fact]
    public void Attendance_is_due_by_the_school_morning_or_the_group_meeting()
    {
        var sunday = new DateOnly(2026, 11, 8);
        Assert.False(OverviewRules.IsSchoolDay(sunday.AddDays(5), [], DayOfWeek.Sunday)); // Friday
        Assert.False(OverviewRules.IsSchoolDay(sunday.AddDays(6), [], DayOfWeek.Sunday)); // Saturday
        Assert.True(OverviewRules.IsSchoolDay(sunday, [], DayOfWeek.Sunday));
        Assert.True(OverviewRules.IsSchoolDay(sunday.AddDays(6), [DayOfWeek.Saturday], DayOfWeek.Sunday)); // history wins

        var ten = new TimeOnly(10, 0);
        Assert.Equal(TodayStatus.Pending, OverviewRules.Section(true, false, new TimeOnly(8, 0), ten));
        Assert.Equal(TodayStatus.Missing, OverviewRules.Section(true, false, new TimeOnly(10, 0), ten));
        Assert.Equal(TodayStatus.Pending, OverviewRules.Section(true, false, new TimeOnly(10, 0), new TimeOnly(11, 30))); // the school's own time
        Assert.Equal(TodayStatus.NotToday, OverviewRules.Section(false, false, new TimeOnly(11, 0), ten));
        Assert.Equal(TodayStatus.Taken, OverviewRules.Section(false, true, new TimeOnly(11, 0), ten));

        Assert.Equal(TodayStatus.NotToday, OverviewRules.Group(null, false, new TimeOnly(18, 0), 30));
        Assert.Equal(TodayStatus.Pending, OverviewRules.Group(new TimeOnly(17, 0), false, new TimeOnly(17, 20), 30));
        Assert.Equal(TodayStatus.Missing, OverviewRules.Group(new TimeOnly(17, 0), false, new TimeOnly(17, 30), 30));
        Assert.Equal(TodayStatus.Missing, OverviewRules.Group(new TimeOnly(17, 0), false, new TimeOnly(17, 10), 10));
        Assert.Equal(TodayStatus.Pending, OverviewRules.Group(new TimeOnly(23, 45), false, new TimeOnly(23, 50), 30)); // no wrap

        Assert.Equal(75, OverviewRules.Rate(2, 1, 1));
        Assert.Null(OverviewRules.Rate(0, 0, 0));
    }
}
