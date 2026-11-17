using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class LegalTests(ApiFactory factory)
{
    [Fact]
    public async Task Sign_up_needs_acceptance_and_records_it_so_nothing_is_pending()
    {
        var client = factory.CreateBrowserClient();
        var userName = $"leg{Guid.NewGuid():N}"[..16];
        var body = new { userName, password = TestUsers.Password, confirmPassword = TestUsers.Password, userType = TestUsers.Student, birthYear = TestUsers.AdultBirthYear };

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Auth/register", body)).StatusCode);
        (await client.PostAsJsonAsync("/api/Auth/register", new { body.userName, body.password, body.confirmPassword, body.userType, body.birthYear, acceptTerms = true })).EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/Auth/login", new { userName, password = TestUsers.Password });
        Assert.False((await TestSchool.DataAsync(login)).GetProperty("legalPending").GetBoolean());
        var status = await TestSchool.GetDataAsync(client, "/api/me/legal");
        Assert.False(status.GetProperty("pending").GetBoolean());
        Assert.Equal(2, status.GetProperty("documents").GetArrayLength());
    }

    [Fact]
    public async Task An_older_acceptance_is_pending_until_the_new_version_is_accepted()
    {
        var client = factory.CreateBrowserClient();
        var userName = (await TestUsers.RegisterAndLoginAsync(client, TestUsers.Teacher, "old")).GetProperty("data").GetProperty("userName").GetString()!;

        using (var scope = factory.Services.CreateScope())
        {
            // Pretend the texts changed: the user only accepted an earlier version.
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var id = await context.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync();
            foreach (var a in await context.LegalAcceptances.Where(a => a.UserId == id).ToListAsync())
                a.Version = "older";
            await context.SaveChangesAsync();
        }

        var login = await factory.CreateBrowserClient().PostAsJsonAsync("/api/Auth/login", new { userName, password = TestUsers.Password });
        Assert.True((await TestSchool.DataAsync(login)).GetProperty("legalPending").GetBoolean());
        Assert.True((await TestSchool.GetDataAsync(client, "/api/me/legal")).GetProperty("pending").GetBoolean());

        (await client.PostAsync("/api/me/legal/accept", null)).EnsureSuccessStatusCode();
        Assert.False((await TestSchool.GetDataAsync(client, "/api/me/legal")).GetProperty("pending").GetBoolean());
    }

    [Fact]
    public async Task A_parent_must_accept_for_a_child_and_it_is_recorded_as_the_parents_act()
    {
        var (parent, _, parentId) = await FamilyTestHelpers.ParentAsync(factory);
        var name = $"kid{Guid.NewGuid():N}"[..16];
        var year = DateTime.UtcNow.Year - 9;

        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/me/children/new", new { userName = name, password = TestUsers.Password, fullName = "Lina", birthYear = year })).StatusCode);
        var created = await parent.PostAsJsonAsync("/api/me/children/new", new { userName = name, password = TestUsers.Password, fullName = "Lina", birthYear = year, acceptTerms = true });
        created.EnsureSuccessStatusCode();
        var childId = Guid.Parse((await TestSchool.DataAsync(created)).GetProperty("userId").GetString()!);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await context.LegalAcceptances.Where(a => a.UserId == childId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(Guid.Parse(parentId), r.AcceptedByUserId));
    }

    [Fact]
    public async Task Data_requests_are_filed_once_handled_by_the_platform_admin_and_audited()
    {
        var client = factory.CreateBrowserClient();
        var userName = (await TestUsers.RegisterAndLoginAsync(client, TestUsers.Student, "req")).GetProperty("data").GetProperty("userName").GetString()!;

        var filed = await client.PostAsJsonAsync("/api/me/data-requests", new { type = "Export" });
        Assert.Equal(HttpStatusCode.OK, filed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/me/data-requests", new { type = "Export" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/data-requests", new { type = "Shred" })).StatusCode);
        var id = (await TestSchool.DataAsync(filed)).GetProperty("id").GetString();
        Assert.Equal("Open", Assert.Single((await TestSchool.GetDataAsync(client, "/api/me/data-requests")).EnumerateArray()).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/platform/data-requests")).StatusCode);

        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();
        var open = (await TestSchool.GetDataAsync(admin, "/api/platform/data-requests?status=Open")).EnumerateArray().ToList();
        Assert.Contains(open, r => r.GetProperty("userName").GetString() == userName);

        (await admin.PostAsJsonAsync($"/api/platform/data-requests/{id}/handle", new { completed = true, note = "Sent by email." })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/platform/data-requests/{id}/handle", new { completed = true })).StatusCode);
        Assert.Equal("Completed", Assert.Single((await TestSchool.GetDataAsync(client, "/api/me/data-requests")).EnumerateArray()).GetProperty("status").GetString());

        var actions = (await TestSchool.GetDataAsync(admin, $"/api/platform/audit?user={userName}")).EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("data-request.export.created", actions);
        Assert.Contains("data-request.export.completed", actions);
    }
}
