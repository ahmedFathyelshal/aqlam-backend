using System.Net;
using System.Net.Http.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PersonalDataTests(ApiFactory factory)
{
    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();
        return admin;
    }

    [Fact]
    public async Task A_user_downloads_their_own_data_and_that_answers_their_open_export_request()
    {
        var school = await TestSchool.CreateAsync(factory);
        var (member, userName, _) = await school.AddMemberAsync("Student", prefix: "exp");
        (await member.PostAsJsonAsync("/api/me/data-requests", new { type = "Export" })).EnsureSuccessStatusCode();

        var data = await TestSchool.GetDataAsync(member, "/api/me/data-export");
        Assert.Equal(userName, data.GetProperty("account").GetProperty("userName").GetString());
        Assert.Single(data.GetProperty("schools").EnumerateArray());
        Assert.Equal(2, data.GetProperty("termsAccepted").GetArrayLength());

        var mine = await TestSchool.GetDataAsync(member, "/api/me/data-requests");
        Assert.Equal("Completed", Assert.Single(mine.EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Admin_can_export_any_account_and_anonymise_it_but_not_an_owner_or_an_admin()
    {
        var school = await TestSchool.CreateAsync(factory);
        var (member, userName, userId) = await school.AddMemberAsync("Student", prefix: "del");
        var admin = await AdminAsync();

        var export = await TestSchool.GetDataAsync(admin, $"/api/platform/users/{userId}/export");
        Assert.Equal(userName, export.GetProperty("account").GetProperty("userName").GetString());

        // Owners must hand over the school first; a platform admin cannot be deleted.
        var ownerId = (await TestSchool.GetDataAsync(school.Owner, "/api/Auth/me")).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/platform/users/{ownerId}/delete", null)).StatusCode);
        var adminId = (await TestSchool.GetDataAsync(admin, "/api/Auth/me")).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/platform/users/{adminId}/delete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"/api/platform/users/{ownerId}/delete", null)).StatusCode);

        (await admin.PostAsync($"/api/platform/users/{userId}/delete", null)).EnsureSuccessStatusCode();
        Assert.NotEqual(HttpStatusCode.OK, (await factory.CreateBrowserClient().PostAsJsonAsync("/api/Auth/login", new { userName, password = TestUsers.Password })).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await member.PostAsync("/api/Auth/refresh", null)).StatusCode);

        var found = await TestSchool.GetDataAsync(admin, $"/api/platform/users?q={userName}");
        Assert.Empty(found.EnumerateArray());
        var actions = (await TestSchool.GetDataAsync(admin, $"/api/platform/audit?action=user.deleted")).EnumerateArray().Select(e => e.GetProperty("target").GetString()).ToList();
        Assert.Contains(actions, a => a is not null && a.StartsWith("deleted-"));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/platform/users/{userId}/delete", null)).StatusCode);
    }
}
