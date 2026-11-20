using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqlamAcademy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReviewTests(ApiFactory factory)
{
    private const int Mathematics = 3;

    [Fact]
    public async Task Only_a_student_who_started_the_course_can_review_it_once_and_edit_or_remove_it()
    {
        var (teacher, org, slug, _) = await ListedCourseAsync("rv1");
        var visitor = factory.CreateBrowserClient();

        var anonymous = await Data(await visitor.GetAsync($"/api/marketplace/courses/{slug}/reviews"));
        Assert.False(anonymous.GetProperty("canReview").GetBoolean());
        Assert.Equal("SignIn", anonymous.GetProperty("cannotReviewReason").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PutAsJsonAsync($"/api/marketplace/courses/{slug}/review", new { stars = 5 })).StatusCode);

        var outsider = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(outsider, TestUsers.Student, "rvo");
        Assert.Equal("NotEnrolled", (await Data(await outsider.GetAsync($"/api/marketplace/courses/{slug}/reviews"))).GetProperty("cannotReviewReason").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await outsider.PutAsJsonAsync($"/api/marketplace/courses/{slug}/review", new { stars = 5 })).StatusCode);

        var (student, studentId) = await EnrolledStudentAsync(org, slug, progress: 0);
        Assert.Equal("TooEarly", (await Data(await student.GetAsync($"/api/marketplace/courses/{slug}/reviews"))).GetProperty("cannotReviewReason").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PutAsJsonAsync($"/api/marketplace/courses/{slug}/review", new { stars = 5 })).StatusCode);

        await SetProgressAsync(studentId, 50);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PutAsJsonAsync($"/api/marketplace/courses/{slug}/review", new { stars = 6 })).StatusCode);
        var saved = await Data(await student.PutAsJsonAsync($"/api/marketplace/courses/{slug}/review", new { stars = 4, comment = "  شرح واضح  " }));
        Assert.Equal(1, saved.GetProperty("total").GetInt32());
        Assert.Equal("شرح واضح", saved.GetProperty("mine").GetProperty("comment").GetString());
        // Too few reviews: the average is not shown yet.
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("summary").GetProperty("average").ValueKind);

        // One review per student: a second save changes it.
        var changed = await Data(await student.PutAsJsonAsync($"/api/marketplace/courses/{slug}/review", new { stars = 5 }));
        Assert.Equal(1, changed.GetProperty("total").GetInt32());
        Assert.Equal(5, changed.GetProperty("mine").GetProperty("stars").GetInt32());

        // The teacher of the course cannot rate it.
        Assert.Equal("Staff", (await Data(await teacher.GetAsync($"/api/marketplace/courses/{slug}/reviews"))).GetProperty("cannotReviewReason").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PutAsJsonAsync($"/api/marketplace/courses/{slug}/review", new { stars = 5 })).StatusCode);

        // A minor's review stays anonymous in the list.
        var listed = await Data(await visitor.GetAsync($"/api/marketplace/courses/{slug}/reviews"));
        Assert.Equal(1, listed.GetProperty("items").GetArrayLength());
        Assert.False(listed.GetProperty("items")[0].GetProperty("mine").GetBoolean());

        var removed = await Data(await student.DeleteAsync($"/api/marketplace/courses/{slug}/review"));
        Assert.Equal(0, removed.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Ratings_appear_only_after_enough_reviews_feed_the_top_list_and_hidden_reviews_do_not_count()
    {
        var (_, org, slug, token) = await ListedCourseAsync("rv2");
        var visitor = factory.CreateBrowserClient();
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();

        var stars = new[] { 5, 4, 5, 4, 5 };
        for (var i = 0; i < stars.Length; i++)
        {
            var (student, id) = await EnrolledStudentAsync(org, slug, progress: 40);
            (await student.PutAsJsonAsync($"/api/marketplace/courses/{slug}/review", new { stars = stars[i], comment = i == 0 ? "ممتاز" : null })).EnsureSuccessStatusCode();
            if (i == 3)
            {
                // Four reviews: no average on the card yet, and not in the top list.
                Assert.Equal(JsonValueKind.Null, Card(await Data(await visitor.GetAsync($"/api/marketplace/courses?q={token}"))).GetProperty("ratingAverage").ValueKind);
                Assert.DoesNotContain((await Data(await visitor.GetAsync($"/api/marketplace/top-teachers?subjectId={Mathematics}"))).EnumerateArray(), t => t.GetProperty("teacher").GetProperty("slug").GetString() == org);
            }
        }

        var card = Card(await Data(await visitor.GetAsync($"/api/marketplace/courses?q={token}")));
        Assert.Equal(4.6, card.GetProperty("ratingAverage").GetDouble());
        Assert.Equal(5, card.GetProperty("ratingCount").GetInt32());
        Assert.Equal(4.6, (await Data(await visitor.GetAsync($"/api/marketplace/teachers/{org}"))).GetProperty("teacher").GetProperty("ratingAverage").GetDouble());

        var top = (await Data(await visitor.GetAsync($"/api/marketplace/top-teachers?subjectId={Mathematics}"))).EnumerateArray().ToList();
        var entry = Assert.Single(top, t => t.GetProperty("teacher").GetProperty("slug").GetString() == org);
        Assert.Equal(5, entry.GetProperty("count").GetInt32());
        Assert.Equal(4.6, entry.GetProperty("average").GetDouble());
        Assert.Equal(1, (await Data(await visitor.GetAsync($"/api/marketplace/courses?q={token}&sort=rating"))).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Data(await visitor.GetAsync($"/api/marketplace/teachers?q={token}&sort=rating"))).GetProperty("total").GetInt32());

        // Only the platform admin moderates; a hidden review no longer counts, so the rating disappears again.
        var reviews = await Data(await admin.GetAsync("/api/platform/marketplace/reviews?take=100"));
        var target = reviews.EnumerateArray().First(r => r.GetProperty("comment").GetString() == "ممتاز");
        var id2 = target.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsJsonAsync($"/api/platform/marketplace/reviews/{id2}/hide", new { reason = "x" })).StatusCode);
        (await admin.PostAsJsonAsync($"/api/platform/marketplace/reviews/{id2}/hide", new { reason = "Contains a phone number." })).EnsureSuccessStatusCode();
        Assert.Equal(4, (await Data(await visitor.GetAsync($"/api/marketplace/courses/{slug}/reviews"))).GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, Card(await Data(await visitor.GetAsync($"/api/marketplace/courses?q={token}"))).GetProperty("ratingAverage").ValueKind);
        var audit = (await Data(await admin.GetAsync("/api/platform/audit?action=review.hidden"))).EnumerateArray().Select(e => e.GetProperty("action").GetString());
        Assert.Contains("review.hidden", audit);

        (await admin.PostAsync($"/api/platform/marketplace/reviews/{id2}/unhide", null)).EnsureSuccessStatusCode();
        Assert.Equal(5, (await Data(await visitor.GetAsync($"/api/marketplace/courses/{slug}/reviews"))).GetProperty("total").GetInt32());
    }

    private static JsonElement Card(JsonElement page) => Assert.Single(page.GetProperty("items").EnumerateArray());

    /// <summary>A free public mathematics course of a teacher who is listed in search.</summary>
    private async Task<(HttpClient Teacher, string Org, string Slug, string Token)> ListedCourseAsync(string prefix)
    {
        var token = $"zq{Guid.NewGuid():N}"[..10];
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, prefix);
        await MarketplaceTests.PublicCourseAsync(teacher, org, $"Review course {token}");
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/public-profile", new { publicName = $"Teacher {token}", headline = "x", city = "Riyadh", gender = "Female", isListed = true })).EnsureSuccessStatusCode();
        var found = await Data(await factory.CreateBrowserClient().GetAsync($"/api/marketplace/courses?q={token}"));
        return (teacher, org, Card(found).GetProperty("slug").GetString()!, token);
    }

    private async Task<(HttpClient Client, Guid UserId)> EnrolledStudentAsync(string org, string slug, int progress)
    {
        var student = factory.CreateBrowserClient();
        var login = await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, "rvs");
        var id = Guid.Parse(login.GetProperty("data").GetProperty("id").GetString()!);
        var course = await Data(await student.GetAsync($"/api/marketplace/courses/{slug}"));
        (await student.PostAsync($"/api/orgs/{org}/learn/courses/{course.GetProperty("courseId").GetString()}/enroll", null)).EnsureSuccessStatusCode();
        await SetProgressAsync(id, progress);
        return (student, id);
    }

    private async Task SetProgressAsync(Guid userId, int progress)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var e in await context.Enrollments.Where(e => e.UserId == userId).ToListAsync())
            e.ProgressPercent = progress;
        await context.SaveChangesAsync();
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
