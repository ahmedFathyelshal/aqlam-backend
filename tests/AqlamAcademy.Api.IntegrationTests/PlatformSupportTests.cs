using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PlatformSupportTests(ApiFactory factory)
{
    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();
        return admin;
    }

    [Fact]
    public async Task Support_finds_locks_unlocks_and_resets_any_account_including_an_owner()
    {
        var school = await TestSchool.CreateAsync(factory);
        var (member, userName, userId) = await school.AddMemberAsync("Student", prefix: "sup");
        var admin = await AdminAsync();

        var found = await TestSchool.GetDataAsync(admin, $"/api/platform/users?q={userName}");
        var row = Assert.Single(found.EnumerateArray());
        Assert.Equal(userId, row.GetProperty("id").GetString());
        Assert.Equal(school.Slug, Assert.Single(row.GetProperty("organizations").EnumerateArray()).GetString());
        Assert.False(row.GetProperty("locked").GetBoolean());

        (await admin.PostAsync($"/api/platform/users/{userId}/lock", null)).EnsureSuccessStatusCode();
        Assert.NotEqual(HttpStatusCode.OK, (await factory.CreateBrowserClient().PostAsJsonAsync("/api/Auth/login", new { userName, password = TestUsers.Password })).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await member.PostAsync("/api/Auth/refresh", null)).StatusCode);

        (await admin.PostAsync($"/api/platform/users/{userId}/unlock", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateBrowserClient().PostAsJsonAsync("/api/Auth/login", new { userName, password = TestUsers.Password })).StatusCode);

        // Platform support can reset an owner, which a school admin cannot.
        var ownerId = (await TestSchool.GetDataAsync(school.Owner, "/api/Auth/me")).GetProperty("id").GetString();
        var reset = await admin.PostAsync($"/api/platform/users/{ownerId}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.False(string.IsNullOrEmpty((await TestSchool.DataAsync(reset)).GetProperty("temporaryPassword").GetString()));

        var actions = (await TestSchool.GetDataAsync(admin, $"/api/platform/audit?user={userName}")).EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("user.locked", actions);
        Assert.Contains("user.unlocked", actions);

        // Only the platform admin may use these tools, and nobody locks themselves.
        Assert.Equal(HttpStatusCode.Forbidden, (await school.Owner.GetAsync("/api/platform/users")).StatusCode);
        var (other, _, _) = await school.AddMemberAsync("Student", prefix: "oth");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync($"/api/platform/users/{ownerId}/lock", null)).StatusCode);
    }

    [Fact]
    public async Task A_suspended_organization_disappears_for_its_members_until_reactivated()
    {
        var school = await TestSchool.CreateAsync(factory);
        var admin = await AdminAsync();

        static bool Lists(JsonElement data, string slug) => data.EnumerateArray().Any(m => m.GetProperty("slug").GetString() == slug);
        Assert.True(Lists(await TestSchool.GetDataAsync(school.Owner, "/api/me/memberships"), school.Slug));

        (await admin.PostAsync($"/api/platform/organizations/{school.Slug}/suspend", null)).EnsureSuccessStatusCode();
        Assert.False(Lists(await TestSchool.GetDataAsync(school.Owner, "/api/me/memberships"), school.Slug));

        (await admin.PostAsync($"/api/platform/organizations/{school.Slug}/reactivate", null)).EnsureSuccessStatusCode();
        Assert.True(Lists(await TestSchool.GetDataAsync(school.Owner, "/api/me/memberships"), school.Slug));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/organizations/no-such-school/suspend", null)).StatusCode);
        var actions = (await TestSchool.GetDataAsync(admin, "/api/platform/audit?action=organization.")).EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("organization.suspended", actions);
        Assert.Contains("organization.reactivated", actions);
    }
}
