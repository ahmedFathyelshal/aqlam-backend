using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuditLogTests(ApiFactory factory)
{
    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();
        return admin;
    }

    [Fact]
    public async Task Sensitive_actions_are_recorded_and_only_the_platform_admin_can_read_them()
    {
        var school = await TestSchool.CreateAsync(factory);
        var (_, userName, userId) = await school.AddMemberAsync("Student", prefix: "aud");

        (await school.Owner.PutAsJsonAsync($"{school.Api}/members/{userId}/roles", new { roles = new[] { "Student", "Parent" } })).EnsureSuccessStatusCode();
        (await school.Owner.PostAsync($"{school.Api}/members/{userId}/reset-password", null)).EnsureSuccessStatusCode();
        (await school.Owner.DeleteAsync($"{school.Api}/members/{userId}")).EnsureSuccessStatusCode();

        var admin = await AdminAsync();
        (await admin.PutAsJsonAsync("/api/platform/marketplace/fees", new { commissionPercent = 12.5m, monthlyStudentFeeSar = 10m, monthlyStudentFeeEgp = 50m })).EnsureSuccessStatusCode();

        var forUser = (await TestSchool.GetDataAsync(admin, $"/api/platform/audit?user={userName}")).EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("member.roles.changed", forUser);
        Assert.Contains("password.reset.admin", forUser);
        Assert.Contains("member.removed", forUser);

        var fees = (await TestSchool.GetDataAsync(admin, "/api/platform/audit?action=fees")).EnumerateArray().ToList();
        Assert.Contains(fees, e => e.GetProperty("action").GetString() == "fees.changed" && e.GetProperty("details").GetString()!.Contains("12.5"));

        // Nothing sensitive leaks into the trail, and ordinary users cannot read it.
        Assert.DoesNotContain("temporary", (await admin.GetStringAsync($"/api/platform/audit?user={userName}")).Replace("Temporary password set", ""), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await school.Owner.GetAsync("/api/platform/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateBrowserClient().GetAsync("/api/platform/audit")).StatusCode);
    }
}
