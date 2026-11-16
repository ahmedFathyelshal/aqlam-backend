using System.Net;
using System.Net.Http.Json;
using System.Text;
using AqlamAcademy.Infrastructure.Persistence;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PasswordRecoveryTests(ApiFactory factory)
{
    [Fact]
    public async Task An_admin_resets_a_members_password_and_the_member_must_change_it()
    {
        var school = await TestSchool.CreateAsync(factory);
        var (student, userName, userId) = await school.AddMemberAsync("Student", prefix: "stu");

        var reset = await school.Owner.PostAsync($"{school.Api}/members/{userId}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var temporary = (await TestSchool.DataAsync(reset)).GetProperty("temporaryPassword").GetString()!;

        // The old password and old session stop working; the temporary one works and forces a change.
        var fresh = factory.CreateBrowserClient();
        Assert.NotEqual(HttpStatusCode.OK, (await fresh.PostAsJsonAsync("/api/Auth/login", new { userName, password = TestUsers.Password })).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await student.PostAsync("/api/Auth/refresh", null)).StatusCode);
        var login = await fresh.PostAsJsonAsync("/api/Auth/login", new { userName, password = temporary });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await TestSchool.DataAsync(login)).GetProperty("mustChangePassword").GetBoolean());

        Assert.True(await HasAuditAsync("password.reset.admin", Guid.Parse(userId)));
    }

    [Fact]
    public async Task Only_admins_can_reset_and_never_an_owner_or_yourself()
    {
        var school = await TestSchool.CreateAsync(factory);
        var (teacher, _, teacherId) = await school.AddMemberAsync("Teacher", prefix: "tch");
        var (_, _, otherId) = await school.AddMemberAsync("Student", prefix: "stu");
        var ownerId = (await TestSchool.GetDataAsync(school.Owner, "/api/Auth/me")).GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsync($"{school.Api}/members/{otherId}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.PostAsync($"{school.Api}/members/{ownerId}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await school.Owner.PostAsync($"{school.Api}/members/{Guid.NewGuid()}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await school.Owner.PostAsync($"{school.Api}/members/{teacherId}/reset-password", null)).StatusCode);
    }

    [Fact]
    public async Task A_member_of_another_school_cannot_be_reset()
    {
        var school = await TestSchool.CreateAsync(factory);
        var elsewhere = await TestSchool.CreateAsync(factory);
        var (_, _, outsiderId) = await elsewhere.AddMemberAsync("Student", prefix: "out");

        Assert.Equal(HttpStatusCode.NotFound, (await school.Owner.PostAsync($"{school.Api}/members/{outsiderId}/reset-password", null)).StatusCode);
    }

    [Fact]
    public async Task A_parent_resets_only_their_own_childs_password()
    {
        var (parent, _, _) = await FamilyTestHelpers.ParentAsync(factory);
        var childName = $"kid{Guid.NewGuid():N}"[..16];
        var created = await parent.PostAsJsonAsync("/api/me/children/new",
            new { userName = childName, password = TestUsers.Password, fullName = "Lina", birthYear = DateTime.UtcNow.Year - 9 });
        created.EnsureSuccessStatusCode();
        var childId = (await TestSchool.DataAsync(created)).GetProperty("userId").GetString();

        var reset = await parent.PostAsync($"/api/me/children/{childId}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var temporary = (await TestSchool.DataAsync(reset)).GetProperty("temporaryPassword").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateBrowserClient().PostAsJsonAsync("/api/Auth/login", new { userName = childName, password = temporary })).StatusCode);

        var (stranger, _, _) = await FamilyTestHelpers.ParentAsync(factory, "str");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/me/children/{childId}/reset-password", null)).StatusCode);
    }

    [Fact]
    public async Task An_emailed_link_sets_a_new_password_once_and_asking_never_reveals_accounts()
    {
        var client = factory.CreateBrowserClient();
        var login = await TestUsers.RegisterAndLoginAsync(client, TestUsers.Student, "rst");
        var userName = login.GetProperty("data").GetProperty("userName").GetString()!;

        // Same answer for a real and an unknown account.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/forgot-password", new { identifier = userName })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/forgot-password", new { identifier = "nobody-here" })).StatusCode);

        Guid userId; string token;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync(userName))!;
            userId = user.Id;
            token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        }

        const string newPassword = "Brand-New-pass1";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Auth/reset-password", new { userId, token = "garbage", newPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Auth/reset-password", new { userId, token, newPassword })).StatusCode);
        // A used link does not work twice.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Auth/reset-password", new { userId, token, newPassword = "Another-pass2x" })).StatusCode);

        var fresh = factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsJsonAsync("/api/Auth/login", new { userName, password = newPassword })).StatusCode);
        Assert.True(await HasAuditAsync("password.reset.email-completed", userId));
    }

    private async Task<bool> HasAuditAsync(string action, Guid targetUserId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.AuditEntries.AnyAsync(a => a.Action == action && a.TargetUserId == targetUserId);
    }
}
