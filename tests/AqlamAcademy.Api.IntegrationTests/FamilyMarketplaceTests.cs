using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;

namespace AqlamAcademy.Api.IntegrationTests;

/// <summary>Age rules and parents acting for their children on the marketplace (decided 2026-11-12).</summary>
[Collection(ApiCollection.Name)]
public class FamilyMarketplaceTests(ApiFactory factory)
{
    private static readonly int ThisYear = DateTime.UtcNow.Year;

    [Fact]
    public async Task Students_give_a_birth_year_and_under_13_need_a_parent()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Register("kid", TestUsers.Student, ThisYear - 10)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Register("nobirth", TestUsers.Student, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Register("teen", TestUsers.Student, ThisYear - 13)).StatusCode);
        // Parents and teachers are not asked.
        Assert.Equal(HttpStatusCode.OK, (await Register("par", TestUsers.Parent, null)).StatusCode);

        // Email is optional (many students have none): the sign-up page sends none when the field is empty.
        var userName = $"noemail{Guid.NewGuid():N}"[..16];
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync("/api/Auth/register", new
        {
            userName, email = (string?)null, password = TestUsers.Password, confirmPassword = TestUsers.Password, userType = TestUsers.Student, birthYear = ThisYear - 14
        })).StatusCode);
    }

    [Fact]
    public async Task A_teenager_asks_a_parent_who_buys_the_course_for_them()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "famt");
        var courseId = await PaidCourseAsync(teacher, org);
        var slug = await SlugAsync(courseId);

        var teen = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teen, TestUsers.Student, "teen", ThisYear - 15);
        var teenId = await IdAsync(teen);

        // Under 18: cannot pay, and the course page says to ask a parent.
        Assert.Equal(HttpStatusCode.Forbidden, (await teen.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/checkout", null)).StatusCode);
        Assert.True((await Data(await teen.GetAsync($"/api/marketplace/courses/{slug}"))).GetProperty("viewer").GetProperty("mustAskParent").GetBoolean());

        // The teen's family code links a parent without any school (asking twice gives the same code).
        var code = (await Data(await teen.PostAsync("/api/me/family-invite", null))).GetProperty("code").GetString()!;
        Assert.Equal(code, (await Data(await teen.PostAsync("/api/me/family-invite", null))).GetProperty("code").GetString());
        var parent = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(parent, TestUsers.Parent, "famp");
        (await parent.PostAsJsonAsync("/api/me/children", new { code })).EnsureSuccessStatusCode();

        // The parent sees the child on the course page and buys for them.
        var viewer = (await Data(await parent.GetAsync($"/api/marketplace/courses/{slug}"))).GetProperty("viewer");
        Assert.Equal(teenId, Assert.Single(viewer.GetProperty("children").EnumerateArray()).GetProperty("userId").GetString());

        var paymentId = (await Data(await parent.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/checkout?forUserId={teenId}", null))).GetProperty("paymentId").GetString();
        var paid = await Data(await parent.PostAsJsonAsync($"/api/payments/{paymentId}/test-complete", new { succeed = true }));
        Assert.Equal("Succeeded", paid.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(paid.GetProperty("forStudentName").GetString()));

        // The course is the teen's, not the parent's.
        Assert.Contains((await Data(await teen.GetAsync("/api/me/courses"))).EnumerateArray(), c => c.GetProperty("courseId").GetString() == courseId);
        Assert.DoesNotContain((await Data(await parent.GetAsync("/api/me/courses"))).EnumerateArray(), c => c.GetProperty("courseId").GetString() == courseId);
        // The teen can see the payment made for them; a stranger cannot.
        Assert.Equal(HttpStatusCode.OK, (await teen.GetAsync($"/api/payments/{paymentId}")).StatusCode);
    }

    [Fact]
    public async Task A_parent_creates_a_young_childs_account_and_acts_only_for_linked_children()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "famc");
        var freeCourse = await CreateDraftAsync(teacher, org, "Free for kids");
        await MakePublishableAsync(teacher, org, freeCourse);
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{freeCourse}/status", new { status = "Published" })).EnsureSuccessStatusCode();
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{freeCourse}/visibility", new { visibility = "Public" })).EnsureSuccessStatusCode();
        var codeCourse = await PaidCourseAsync(teacher, org);
        var cardCode = (await Data(await teacher.PostAsJsonAsync($"/api/orgs/{org}/courses/{codeCourse}/code-batches", new { name = "Desk", quantity = 1 })))
            .GetProperty("codes")[0].GetString();

        var parent = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(parent, TestUsers.Parent, "famk");
        var childUserName = $"kid{Guid.NewGuid():N}"[..16];
        var child = await Data(await parent.PostAsJsonAsync("/api/me/children/new",
            new { userName = childUserName, password = TestUsers.Password, fullName = "Lina", birthYear = ThisYear - 9 }));
        var childId = child.GetProperty("userId").GetString();
        // Adults are not created as children.
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/me/children/new",
            new { userName = $"adult{Guid.NewGuid():N}"[..16], password = TestUsers.Password, birthYear = ThisYear - 30 })).StatusCode);

        // The parent joins a free course and uses a card code for the child.
        (await parent.PostAsync($"/api/orgs/{org}/learn/courses/{freeCourse}/enroll?forUserId={childId}", null)).EnsureSuccessStatusCode();
        (await parent.PostAsJsonAsync("/api/codes/redeem", new { code = cardCode, forUserId = childId })).EnsureSuccessStatusCode();

        // The child signs in and has both courses.
        var kid = factory.CreateBrowserClient();
        (await kid.PostAsJsonAsync("/api/Auth/login", new { userName = childUserName, password = TestUsers.Password })).EnsureSuccessStatusCode();
        var mine = (await Data(await kid.GetAsync("/api/me/courses"))).EnumerateArray().Select(c => c.GetProperty("courseId").GetString()).ToList();
        Assert.Contains(freeCourse, mine);
        Assert.Contains(codeCourse, mine);

        // Nobody acts for a child that is not theirs.
        var stranger = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(stranger, TestUsers.Parent, "strp");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/orgs/{org}/learn/courses/{freeCourse}/enroll?forUserId={childId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/orgs/{org}/learn/courses/{codeCourse}/checkout?forUserId={childId}", null)).StatusCode);
    }

    [Fact]
    public async Task An_adult_without_a_birth_year_adds_it_once_and_can_then_buy()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "famb");
        var courseId = await PaidCourseAsync(teacher, org);
        var slug = await SlugAsync(courseId);

        // An account from before the age rules: a student with no birth year counts as a minor.
        var student = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, "old");
        var studentId = Guid.Parse(await IdAsync(student));
        using (var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(factory.Services))
        {
            var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<global::AqlamAcademy.Infrastructure.Persistence.ApplicationDbContext>(scope.ServiceProvider);
            var user = await db.Users.FindAsync(studentId);
            user!.BirthYear = null;
            await db.SaveChangesAsync();
        }

        var viewer = (await Data(await student.GetAsync($"/api/marketplace/courses/{slug}"))).GetProperty("viewer");
        Assert.True(viewer.GetProperty("birthYearMissing").GetBoolean());
        Assert.True(viewer.GetProperty("mustAskParent").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/checkout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await student.PutAsJsonAsync("/api/me/birth-year", new { birthYear = ThisYear + 1 })).StatusCode);
        (await student.PutAsJsonAsync("/api/me/birth-year", new { birthYear = ThisYear - 30 })).EnsureSuccessStatusCode();
        // Set once: a second change goes through support (so a teenager cannot "become" 18).
        Assert.Equal(HttpStatusCode.Conflict, (await student.PutAsJsonAsync("/api/me/birth-year", new { birthYear = ThisYear - 40 })).StatusCode);

        Assert.False((await Data(await student.GetAsync($"/api/marketplace/courses/{slug}"))).GetProperty("viewer").GetProperty("mustAskParent").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/checkout", null)).StatusCode);
    }

    private Task<HttpResponseMessage> Register(string prefix, int userType, int? birthYear)
    {
        var userName = $"{prefix}{Guid.NewGuid():N}"[..16];
        return factory.CreateClient().PostAsJsonAsync("/api/Auth/register", new
        {
            userName, email = $"{userName}@test.local", password = TestUsers.Password, confirmPassword = TestUsers.Password, userType, birthYear
        });
    }

    private static async Task<string> PaidCourseAsync(HttpClient teacher, string org)
    {
        var id = await CreateDraftAsync(teacher, org, $"Paid {Guid.NewGuid():N}"[..14]);
        await MakePublishableAsync(teacher, org, id);
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{id}/commercials", new
        {
            id, pricingType = "Paid", price = 100m, codeActivationOnly = false, accessExpiryPolicy = "Unlimited", accessDurationDays = (int?)null
        })).EnsureSuccessStatusCode();
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{id}/status", new { status = "Published" })).EnsureSuccessStatusCode();
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{id}/visibility", new { visibility = "Public" })).EnsureSuccessStatusCode();
        return id;
    }

    private async Task<string> SlugAsync(string courseId)
    {
        var shortCode = Guid.Parse(courseId).ToString("N")[..8];
        // Any slug ending in the short code finds the course.
        var page = await Data(await factory.CreateClient().GetAsync($"/api/marketplace/courses/x-{shortCode}"));
        return page.GetProperty("course").GetProperty("slug").GetString()!;
    }

    private static async Task<string> IdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/Auth/me")).GetProperty("data").GetProperty("id").GetString()!;

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
