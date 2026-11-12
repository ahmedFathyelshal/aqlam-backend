using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MarketplaceTests(ApiFactory factory)
{
    private const int Mathematics = 3;

    [Fact]
    public async Task Public_courses_are_found_before_sign_in_with_arabic_spelling_variants()
    {
        var token = Token();
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "mkt");
        var courseId = await PublicCourseAsync(teacher, org, $"أساسيات الرياضيات {token}", previewFirstLesson: true);
        var visitor = factory.CreateBrowserClient();

        // Different alef, no article, other case: still found.
        var found = await Data(await visitor.GetAsync($"/api/marketplace/courses?q={Uri.EscapeDataString($"اساسيات رياضيات {token.ToUpperInvariant()}")}"));
        var card = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal(courseId, (await Data(await visitor.GetAsync($"/api/marketplace/courses/{card.GetProperty("slug").GetString()}"))).GetProperty("courseId").GetString());
        // Usernames are never shown publicly.
        Assert.DoesNotContain("mkt", card.GetProperty("teacherName").GetString());

        // Subject words find it too, and filters narrow it.
        Assert.Equal(1, (await Data(await visitor.GetAsync($"/api/marketplace/courses?q=mathematics%20{token}&subjectId={Mathematics}&free=true"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Data(await visitor.GetAsync($"/api/marketplace/courses?q={token}&country=EG"))).GetProperty("total").GetInt32());

        // Course page: outline, free preview open to visitors, other lessons not.
        var slug = card.GetProperty("slug").GetString()!;
        var page = await Data(await visitor.GetAsync($"/api/marketplace/courses/{slug}"));
        Assert.False(page.GetProperty("viewer").GetProperty("signedIn").GetBoolean());
        var lessons = page.GetProperty("chapters")[0].GetProperty("lessons").EnumerateArray().ToList();
        Assert.Equal(2, lessons.Count);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync($"/api/marketplace/courses/{slug}/lessons/{lessons[0].GetProperty("id").GetString()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/marketplace/courses/{slug}/lessons/{lessons[1].GetProperty("id").GetString()}")).StatusCode);

        // An old link still works after the title changes; the page gives the new address.
        var draft = await GetDraftAsync(teacher, org, courseId);
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/identity", new
        {
            id = courseId, title = $"Renamed {token}", curriculumId = Int(draft, "curriculumId"), stageId = Int(draft, "stageId"),
            gradeId = Int(draft, "gradeId"), subjectId = Mathematics
        })).EnsureSuccessStatusCode();
        var renamed = await Data(await visitor.GetAsync($"/api/marketplace/courses/{slug}"));
        Assert.StartsWith("renamed-", renamed.GetProperty("course").GetProperty("slug").GetString());

        // Going private takes it off the marketplace.
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/visibility", new { visibility = "Private" })).EnsureSuccessStatusCode();
        Assert.Equal(0, (await Data(await visitor.GetAsync($"/api/marketplace/courses?q={token}"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/marketplace/courses/{slug}")).StatusCode);
    }

    [Fact]
    public async Task Drafts_and_school_courses_never_appear()
    {
        var token = Token();
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "mktd");
        var draftId = await CreateDraftAsync(teacher, org, $"Draft {token}");
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{draftId}/visibility", new { visibility = "Public" })).EnsureSuccessStatusCode();

        var school = await TestSchool.CreateAsync(factory);
        var schoolCourse = await CreateDraftAsync(school.Owner, school.Slug, $"School {token}");
        await MakePublishableAsync(school.Owner, school.Slug, schoolCourse);
        (await school.Owner.PutAsJsonAsync($"{school.Api}/courses/{schoolCourse}/status", new { status = "Published" })).EnsureSuccessStatusCode();
        Assert.False((await school.Owner.PutAsJsonAsync($"{school.Api}/courses/{schoolCourse}/visibility", new { visibility = "Public" })).IsSuccessStatusCode);

        var visitor = factory.CreateBrowserClient();
        Assert.Equal(0, (await Data(await visitor.GetAsync($"/api/marketplace/courses?q={token}"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Teachers_choose_to_be_listed_with_a_public_name_and_gender_and_students_are_never_found()
    {
        var token = Token();
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "mktt");
        await PublicCourseAsync(teacher, org, $"Algebra {token}");
        var visitor = factory.CreateBrowserClient();

        var profile = await Data(await teacher.GetAsync($"/api/orgs/{org}/public-profile"));
        Assert.Contains("publicName", profile.GetProperty("missingForListing").EnumerateArray().Select(e => e.GetString()));

        // Gender is required before appearing in search.
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PutAsJsonAsync($"/api/orgs/{org}/public-profile",
            new { publicName = $"أ. هدى {token}", isListed = true })).StatusCode);
        var listed = await Data(await teacher.PutAsJsonAsync($"/api/orgs/{org}/public-profile",
            new { publicName = $"أ. هدى {token}", headline = "معلمة رياضيات", city = "الرياض", gender = "Female", isListed = true }));
        Assert.True(listed.GetProperty("isListed").GetBoolean());

        var teachers = await Data(await visitor.GetAsync($"/api/marketplace/teachers?q={Uri.EscapeDataString($"هدي {token}")}&teacherGender=Female&subjectId={Mathematics}"));
        var card = Assert.Single(teachers.GetProperty("items").EnumerateArray());
        Assert.Equal(org, card.GetProperty("slug").GetString());
        Assert.Equal(0, (await Data(await visitor.GetAsync($"/api/marketplace/teachers?q={token}&teacherGender=Male"))).GetProperty("total").GetInt32());

        var page = await Data(await visitor.GetAsync($"/api/marketplace/teachers/{org}"));
        Assert.Equal(1, page.GetProperty("courses").GetArrayLength());
        Assert.Equal(org, page.GetProperty("courses")[0].GetProperty("teacherSlug").GetString());

        // A student who joined the course is never searchable.
        var student = factory.CreateBrowserClient();
        var studentName = (await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, $"stu{token[..4]}")).GetProperty("data").GetProperty("userName").GetString()!;
        Assert.Equal(0, (await Data(await visitor.GetAsync($"/api/marketplace/teachers?q={studentName}"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Data(await visitor.GetAsync($"/api/marketplace/courses?q={studentName}"))).GetProperty("total").GetInt32());

        // Unlisting removes the teacher page.
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/public-profile", new { publicName = $"أ. هدى {token}", gender = "Female", isListed = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/marketplace/teachers/{org}")).StatusCode);
    }

    [Fact]
    public async Task Only_the_platform_admin_features_teachers_and_picks_are_public()
    {
        var token = Token();
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "mktf");
        await PublicCourseAsync(teacher, org, $"Geometry {token}");
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/public-profile", new { publicName = $"Mr Ali {token}", gender = "Male", isListed = true })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync("/api/platform/marketplace/featured", new { slug = org, subjectId = Mathematics, order = 1 })).StatusCode);

        var admin = await AdminAsync();
        var pick = await Data(await admin.PostAsJsonAsync("/api/platform/marketplace/featured", new { slug = org, subjectId = Mathematics, order = 1 }));

        var visitor = factory.CreateBrowserClient();
        var featured = (await Data(await visitor.GetAsync($"/api/marketplace/featured?subjectId={Mathematics}"))).EnumerateArray().ToList();
        Assert.Contains(featured, f => f.GetProperty("teacher").GetProperty("slug").GetString() == org);

        (await admin.DeleteAsync($"/api/platform/marketplace/featured/{pick.GetProperty("id").GetString()}")).EnsureSuccessStatusCode();
        featured = (await Data(await visitor.GetAsync($"/api/marketplace/featured?subjectId={Mathematics}"))).EnumerateArray().ToList();
        Assert.DoesNotContain(featured, f => f.GetProperty("teacher").GetProperty("slug").GetString() == org);
    }

    [Fact]
    public async Task Buying_a_course_with_the_test_provider_books_commission_once_and_gives_access()
    {
        var token = Token();
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "mktp");
        var courseId = await PublicCourseAsync(teacher, org, $"Physics {token}", price: 200m);
        var admin = await AdminAsync();
        (await admin.PutAsJsonAsync($"/api/platform/marketplace/organizations/{org}/commission", new { commissionPercent = 20m })).EnsureSuccessStatusCode();

        var student = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, "buyer");
        // Free enrolment is refused for a paid course.
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/enroll", null)).StatusCode);

        var checkout = await Data(await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/checkout", null));
        var paymentId = checkout.GetProperty("paymentId").GetString()!;
        Assert.Equal(200m, checkout.GetProperty("amount").GetDecimal());
        Assert.Equal($"/checkout/test/{paymentId}", checkout.GetProperty("redirectUrl").GetString());
        // A second click resumes the same checkout.
        Assert.Equal(paymentId, (await Data(await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/checkout", null))).GetProperty("paymentId").GetString());
        // No access before the provider confirms.
        Assert.DoesNotContain((await Data(await student.GetAsync("/api/me/courses"))).EnumerateArray(), c => c.GetProperty("courseId").GetString() == courseId);
        var slug = (await Data(await student.GetAsync($"/api/marketplace/courses?q={token}"))).GetProperty("items")[0].GetProperty("slug").GetString();
        Assert.True((await Data(await student.GetAsync($"/api/marketplace/courses/{slug}"))).GetProperty("viewer").GetProperty("hasPendingPayment").GetBoolean());

        // Someone else cannot complete the student's payment.
        var stranger = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(stranger, TestUsers.Student, "strng");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/payments/{paymentId}/test-complete", new { succeed = true })).StatusCode);

        var paid = await Data(await student.PostAsJsonAsync($"/api/payments/{paymentId}/test-complete", new { succeed = true }));
        Assert.Equal("Succeeded", paid.GetProperty("status").GetString());

        // The provider repeats its notification (signed): booked once, and a bad signature is refused.
        var body = JsonSerializer.Serialize(new { reference = $"test_{Guid.Parse(paymentId):N}", status = "paid", amount = 200m, currency = "SAR" });
        Assert.Equal(HttpStatusCode.OK, (await Webhook(body, Sign(body))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Webhook(body, Sign(body))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Webhook(body, "bad")).StatusCode);

        var mine = await Data(await student.GetAsync("/api/me/courses"));
        Assert.Contains(mine.EnumerateArray(), c => c.GetProperty("courseId").GetString() == courseId);

        var earnings = await Data(await teacher.GetAsync($"/api/orgs/{org}/earnings"));
        var sar = Assert.Single(earnings.GetProperty("totals").EnumerateArray());
        Assert.Equal(200m, sar.GetProperty("gross").GetDecimal());
        Assert.Equal(40m, sar.GetProperty("commission").GetDecimal());
        Assert.Equal(160m, sar.GetProperty("net").GetDecimal());
        Assert.Single(earnings.GetProperty("recentSales").EnumerateArray());

        // Buying again is refused; a declined payment gives no access.
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/checkout", null)).StatusCode);
        var other = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(other, TestUsers.Student, "declin");
        var otherPayment = (await Data(await other.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/checkout", null))).GetProperty("paymentId").GetString();
        var declined = await Data(await other.PostAsJsonAsync($"/api/payments/{otherPayment}/test-complete", new { succeed = false }));
        Assert.Equal("Failed", declined.GetProperty("status").GetString());
        Assert.DoesNotContain((await Data(await other.GetAsync("/api/me/courses"))).EnumerateArray(), c => c.GetProperty("courseId").GetString() == courseId);
    }

    [Fact]
    public async Task Activation_codes_are_single_use_limited_and_count_toward_the_monthly_fee()
    {
        var token = Token();
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "mktc");
        var courseId = await PublicCourseAsync(teacher, org, $"Chemistry {token}", price: 150m, codesOnly: true);
        var admin = await AdminAsync();
        (await admin.PutAsJsonAsync("/api/platform/marketplace/fees", new { commissionPercent = 15m, monthlyStudentFeeSar = 5m, monthlyStudentFeeEgp = 20m })).EnsureSuccessStatusCode();

        var created = await Data(await teacher.PostAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/code-batches", new { name = "مكتبة النور", quantity = 3 }));
        var codes = created.GetProperty("codes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(3, codes.Count);
        Assert.All(codes, c => Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", c));
        var batchId = created.GetProperty("batch").GetProperty("id").GetString();

        var student = factory.CreateBrowserClient();
        var studentName = (await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, "codst")).GetProperty("data").GetProperty("userName").GetString();
        // Typed in lower case without dashes still works.
        var redeemed = await Data(await student.PostAsJsonAsync("/api/codes/redeem", new { code = codes[0].Replace("-", "").ToLowerInvariant(), courseId }));
        Assert.Equal(org, redeemed.GetProperty("organizationSlug").GetString());

        var second = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(second, TestUsers.Student, "codsb");
        Assert.Equal(HttpStatusCode.Conflict, (await second.PostAsJsonAsync("/api/codes/redeem", new { code = codes[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await second.PostAsJsonAsync("/api/codes/redeem", new { code = codes[1], courseId = Guid.NewGuid() })).StatusCode);

        var lookup = await Data(await teacher.GetAsync($"/api/orgs/{org}/courses/{courseId}/code-batches/lookup?code={codes[0]}"));
        Assert.Equal("Used", lookup.GetProperty("status").GetString());
        Assert.Equal(studentName, lookup.GetProperty("redeemedByUserName").GetString());

        // Cancelling the batch stops its unused codes.
        (await teacher.PostAsync($"/api/orgs/{org}/courses/{courseId}/code-batches/{batchId}/cancel", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await second.PostAsJsonAsync("/api/codes/redeem", new { code = codes[2] })).StatusCode);
        var batches = await Data(await teacher.GetAsync($"/api/orgs/{org}/courses/{courseId}/code-batches"));
        Assert.Equal(1, batches[0].GetProperty("redeemed").GetInt32());

        // Guessing is slowed down: after five wrong codes the student must wait.
        var guesser = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(guesser, TestUsers.Student, "guess");
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await guesser.PostAsJsonAsync("/api/codes/redeem", new { code = "AAAA-BBBB-CCCC" })).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await guesser.PostAsJsonAsync("/api/codes/redeem", new { code = codes[1] })).StatusCode);

        // The code student counts toward the monthly fee once they study this month.
        var fee = (await Data(await teacher.GetAsync($"/api/orgs/{org}/earnings"))).GetProperty("monthlyFee");
        Assert.Equal(0, fee.GetProperty("countedStudents").GetInt32());
        var lessonId = (await Data(await student.GetAsync($"/api/orgs/{org}/learn/courses/{courseId}"))).GetProperty("chapters")[0].GetProperty("lessons")[0].GetProperty("id").GetString();
        (await student.PostAsJsonAsync($"/api/orgs/{org}/learn/lessons/{lessonId}/progress", new { positionSeconds = 30, watchedPercent = 20 })).EnsureSuccessStatusCode();
        fee = (await Data(await teacher.GetAsync($"/api/orgs/{org}/earnings"))).GetProperty("monthlyFee");
        Assert.Equal(1, fee.GetProperty("countedStudents").GetInt32());
        Assert.Equal(5m, fee.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Code_batches_are_for_course_managers_only()
    {
        var center = await TestSchool.CreateAsync(factory, "Center");
        (await center.Owner.PutAsJsonAsync($"{center.Api}/settings",
            new { name = "Center", timeZone = "Africa/Cairo", calendar = "Gregorian", weekStartDay = "Saturday", teachersCreateCourses = true })).EnsureSuccessStatusCode();
        var teacher = await center.AddMemberAsync("Teacher", prefix: "cbt");
        var courseId = await CreateDraftAsync(teacher.Client, center.Slug, "Center course");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync($"{center.Api}/courses/{courseId}/code-batches", new { name = "x", quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await center.Owner.PostAsJsonAsync($"{center.Api}/courses/{courseId}/code-batches", new { name = "x", quantity = 1 })).StatusCode);
    }

    // ---------- helpers ----------

    private static string Token() => $"zq{Guid.NewGuid():N}"[..10];

    private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    /// <summary>A published, public mathematics course with two video lessons.</summary>
    private static async Task<string> PublicCourseAsync(HttpClient teacher, string org, string title, decimal? price = null, bool codesOnly = false, bool previewFirstLesson = false)
    {
        var courseId = await CreateDraftAsync(teacher, org, title);
        await MakePublishableAsync(teacher, org, courseId);
        var draft = await GetDraftAsync(teacher, org, courseId);
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/identity", new
        {
            id = courseId, title, curriculumId = Int(draft, "curriculumId"), stageId = Int(draft, "stageId"), gradeId = Int(draft, "gradeId"), subjectId = Mathematics
        })).EnsureSuccessStatusCode();
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/commercials", new
        {
            id = courseId, pricingType = price is null ? "Free" : "Paid", price, codeActivationOnly = codesOnly, accessExpiryPolicy = "Unlimited", accessDurationDays = (int?)null
        })).EnsureSuccessStatusCode();

        var version = (await GetDraftAsync(teacher, org, courseId)).GetProperty("curriculumVersion").GetInt32();
        var first = new
        {
            title = "Welcome", type = "Video", videoUrl = "https://video.example/1", durationInMinutes = 10, isFreePreview = previewFirstLesson,
            isPublished = true, unlockDaysAfterEnrollment = 0, requirePreviousLessonCompletion = false
        };
        (await SyncAsync(teacher, org, courseId, version, [new { title = "Unit 1", lessons = new object[] { first, Lesson("Equations") } }])).EnsureSuccessStatusCode();
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/status", new { status = "Published" })).EnsureSuccessStatusCode();
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/courses/{courseId}/visibility", new { visibility = "Public" })).EnsureSuccessStatusCode();
        return courseId;
    }

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();
        return admin;
    }

    private Task<HttpResponseMessage> Webhook(string body, string signature)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhooks/test") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Signature", signature);
        return client.SendAsync(request);
    }

    private static string Sign(string body) =>
        global::AqlamAcademy.Infrastructure.Persistence.Payments.TestPaymentProvider.Sign(body, "development-only-test-provider-secret");

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
