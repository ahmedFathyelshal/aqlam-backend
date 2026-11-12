using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_returns_profile_and_never_exposes_tokens_in_the_body()
    {
        var client = factory.CreateBrowserClient();

        var body = await TestUsers.RegisterAndLoginAsync(client, TestUsers.Teacher, "login");

        var raw = body.GetRawText();
        Assert.DoesNotContain("accessToken", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Teacher", body.GetProperty("data").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Refresh_keeps_the_user_profile_with_roles()
    {
        var client = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(client, TestUsers.Student, "refresh");

        var response = await client.PostAsync("/api/Auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Student", body.GetProperty("data").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Refresh_without_cookie_returns_401()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.PostAsync("/api/Auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_reuse_of_a_just_rotated_token_returns_409_and_keeps_the_session()
    {
        // Simulates two tabs sending the same refresh cookie: the second arrives after the first rotated it.
        var client = factory.CreateClient(new() { HandleCookies = false });
        var userName = $"tabs{Guid.NewGuid():N}"[..20];
        const string password = "Passw0rd!x";
        (await client.PostAsJsonAsync("/api/Auth/register", new { userName, email = $"{userName}@test.local", password, confirmPassword = password, userType = TestUsers.Student, birthYear = TestUsers.AdultBirthYear }))
            .EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/Auth/login", new { userName, password });
        var loginCookies = ReadSetCookies(login);

        var first = await SendWithCookiesAsync(client, HttpMethod.Post, "/api/Auth/refresh", loginCookies);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var rotatedCookies = ReadSetCookies(first);

        // Second "tab" still holds the pre-rotation refresh cookie.
        var second = await SendWithCookiesAsync(client, HttpMethod.Post, "/api/Auth/refresh", loginCookies);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // The rotated session must still be alive (no replay-attack termination).
        var again = await SendWithCookiesAsync(client, HttpMethod.Post, "/api/Auth/refresh", rotatedCookies);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Register_validation_errors_return_400_with_field_keys()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.PostAsJsonAsync("/api/Auth/register", new
        {
            userName = "",
            email = "not-an-email",
            password = "short",
            confirmPassword = "different",
            userType = TestUsers.Student
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("userName", out _));
        Assert.True(errors.TryGetProperty("password", out _));
        Assert.True(errors.TryGetProperty("email", out _));
    }

    private static Dictionary<string, string> ReadSetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0].Split('=', 2)).ToDictionary(p => p[0], p => p[1])
            : new Dictionary<string, string>();

    private static Task<HttpResponseMessage> SendWithCookiesAsync(
        HttpClient client, HttpMethod method, string url, Dictionary<string, string> cookies)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}")));
        return client.SendAsync(request);
    }
}
