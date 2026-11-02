using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

public static class TestUsers
{
    public const int Student = 1;
    public const int Teacher = 2;
    public const string Password = "Passw0rd!x";

    /// <summary>Registers a unique user and logs the client in (auth cookies are kept by the client).</summary>
    public static async Task<JsonElement> RegisterAndLoginAsync(HttpClient client, int userType, string prefix)
    {
        var userName = $"{prefix}{Guid.NewGuid():N}"[..20];
        const string password = Password;

        var register = await client.PostAsJsonAsync("/api/Auth/register", new
        {
            userName,
            email = $"{userName}@test.local",
            password,
            confirmPassword = password,
            userType
        });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/Auth/login", new { userName, password });
        login.EnsureSuccessStatusCode();

        return await login.Content.ReadFromJsonAsync<JsonElement>();
    }
}
