using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PlatformOrganizationTests(ApiFactory factory)
{
    [Fact]
    public async Task Non_admin_cannot_create_organizations()
    {
        var teacher = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(teacher, TestUsers.Teacher, "notadm");

        var response = await teacher.PostAsJsonAsync("/api/platform/organizations", NewSchool(UniqueSlug(), "anyone"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_creates_a_private_school_with_subdomain_and_owner()
    {
        var admin = await LoginAdminAsync();
        var owner = factory.CreateBrowserClient();
        var ownerName = await RegisterUserAsync(owner, TestUsers.Teacher, "own");
        var slug = UniqueSlug();

        var response = await admin.PostAsJsonAsync("/api/platform/organizations", NewSchool(slug, ownerName));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var org = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("School", org.GetProperty("type").GetString());
        Assert.Equal("Disabled", org.GetProperty("publicPublishing").GetString());
        Assert.Equal("SAR", org.GetProperty("currency").GetString());
        Assert.Equal($"{slug}.localhost", org.GetProperty("primaryHost").GetString());

        var memberships = (await owner.GetFromJsonAsync<JsonElement>("/api/me/memberships")).GetProperty("data");
        var school = memberships.EnumerateArray().Single(m => m.GetProperty("slug").GetString() == slug);
        var roles = school.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("Owner", roles);
        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task Duplicate_slug_returns_409_and_reserved_slug_returns_400()
    {
        var admin = await LoginAdminAsync();
        var ownerName = await RegisterUserAsync(factory.CreateBrowserClient(), TestUsers.Teacher, "dup");
        var slug = UniqueSlug();
        (await admin.PostAsJsonAsync("/api/platform/organizations", NewSchool(slug, ownerName))).EnsureSuccessStatusCode();

        var duplicate = await admin.PostAsJsonAsync("/api/platform/organizations", NewSchool(slug, ownerName));
        var reserved = await admin.PostAsJsonAsync("/api/platform/organizations", NewSchool("admin", ownerName));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reserved.StatusCode);
        var errors = (await reserved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("slug", out _));
    }

    [Fact]
    public async Task Organization_is_resolved_from_its_subdomain()
    {
        var admin = await LoginAdminAsync();
        var owner = factory.CreateBrowserClient();
        var outsider = factory.CreateBrowserClient();
        var ownerName = await RegisterUserAsync(owner, TestUsers.Teacher, "host");
        var outsiderName = await RegisterUserAsync(outsider, TestUsers.Teacher, "hostout");
        var slug = UniqueSlug();
        (await admin.PostAsJsonAsync("/api/platform/organizations", NewSchool(slug, ownerName))).EnsureSuccessStatusCode();

        // Sessions are bound to the domain they were created on, so both sign in on the school's domain.
        (await SendOnHostAsync(owner, $"{slug}.localhost", "/api/Auth/login", new { userName = ownerName, password = TestUsers.Password })).EnsureSuccessStatusCode();
        (await SendOnHostAsync(outsider, $"{slug}.localhost", "/api/Auth/login", new { userName = outsiderName, password = TestUsers.Password })).EnsureSuccessStatusCode();

        var byOwner = await SendOnHostAsync(owner, $"{slug}.localhost", "/api/courses/draft", new { title = "Grade 7 Science" });
        var byOutsider = await SendOnHostAsync(outsider, $"{slug}.localhost", "/api/courses/draft", new { title = "Intruder" });

        Assert.Equal(HttpStatusCode.OK, byOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byOutsider.StatusCode);
    }

    private async Task<HttpClient> LoginAdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        var login = await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword });
        login.EnsureSuccessStatusCode();
        return admin;
    }

    private static async Task<string> RegisterUserAsync(HttpClient client, int userType, string prefix)
    {
        var body = await TestUsers.RegisterAndLoginAsync(client, userType, prefix);
        return body.GetProperty("data").GetProperty("userName").GetString()!;
    }

    private static Task<HttpResponseMessage> SendOnHostAsync(HttpClient client, string host, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Host = host;
        return client.SendAsync(request);
    }

    private static object NewSchool(string slug, string ownerUserName) => new
    {
        name = "Nile International School",
        slug,
        type = "School",
        countryCode = "SA",
        ownerUserName
    };

    private static string UniqueSlug() => $"school-{Guid.NewGuid():N}"[..20];
}
