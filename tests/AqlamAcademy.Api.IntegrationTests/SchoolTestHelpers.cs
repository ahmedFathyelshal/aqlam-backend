using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

/// <summary>A school created by the platform admin, with helpers to add people to it.</summary>
public sealed class TestSchool(ApiFactory factory, HttpClient owner, string slug)
{
    public HttpClient Owner { get; } = owner;
    public string Slug { get; } = slug;
    public string Api => $"/api/orgs/{Slug}";

    public static async Task<TestSchool> CreateAsync(ApiFactory factory, string type = "School")
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();

        var owner = factory.CreateBrowserClient();
        var ownerName = await RegisterAsync(owner, TestUsers.Teacher, "sown");
        var slug = $"school-{Guid.NewGuid():N}"[..20];
        (await admin.PostAsJsonAsync("/api/platform/organizations", new { name = "Al Rowad School", slug, type, countryCode = "SA", ownerUserName = ownerName }))
            .EnsureSuccessStatusCode();

        return new TestSchool(factory, owner, slug);
    }

    /// <summary>Registers a user, adds them with the given roles and optional gender. Returns their client, username and id.</summary>
    public async Task<(HttpClient Client, string UserName, string UserId)> AddMemberAsync(string role, string? gender = null, string prefix = "m")
    {
        var client = factory.CreateBrowserClient();
        var userType = role is "Student" ? TestUsers.Student : TestUsers.Teacher;
        var login = await TestUsers.RegisterAndLoginAsync(client, userType, prefix);
        var userName = login.GetProperty("data").GetProperty("userName").GetString()!;
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/Auth/me")).GetProperty("data").GetProperty("id").GetString()!;

        (await Owner.PostAsJsonAsync($"{Api}/members", new { userName, roles = new[] { role } })).EnsureSuccessStatusCode();
        if (gender is not null)
            (await Owner.PutAsJsonAsync($"{Api}/members/{userId}/gender", new { gender })).EnsureSuccessStatusCode();

        return (client, userName, userId);
    }

    public async Task<string> CreateClassAsync(string name, string gender = "Mixed", string? branchId = null, int? capacity = null, string kind = "Section")
    {
        var response = await Owner.PostAsJsonAsync($"{Api}/classes", new { name, kind, gender, branchId, capacity });
        response.EnsureSuccessStatusCode();
        return (await DataAsync(response)).GetProperty("id").GetString()!;
    }

    public static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

    public static async Task<JsonElement> GetDataAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("data");

    private static async Task<string> RegisterAsync(HttpClient client, int userType, string prefix) =>
        (await TestUsers.RegisterAndLoginAsync(client, userType, prefix)).GetProperty("data").GetProperty("userName").GetString()!;
}
