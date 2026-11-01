using System.Net;
using System.Net.Http.Json;

namespace AqlamAcademy.Api.IntegrationTests;

/// <summary>
/// Sessions created on an organization's own domain are bound to it (otid claim), and main-domain sessions
/// cannot be used on an organization's domain. Cookies are handled manually to model per-host cookie jars.
/// </summary>
[Collection(ApiCollection.Name)]
public class HostBoundSessionTests(ApiFactory factory)
{
    private const string MainHost = "localhost";

    [Fact]
    public async Task Session_from_organization_domain_works_only_on_that_domain()
    {
        var (userName, password, schoolHost) = await CreateSchoolWithOwnerAsync();
        var client = factory.CreateClient(new() { HandleCookies = false });

        var login = await SendAsync(client, HttpMethod.Post, "/api/Auth/login", schoolHost, new(), new { userName, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var schoolCookies = ReadSetCookies(login);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", schoolHost, schoolCookies)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", MainHost, schoolCookies)).StatusCode);

        // A refreshed session stays bound to the same domain.
        var refresh = await SendAsync(client, HttpMethod.Post, "/api/Auth/refresh", schoolHost, schoolCookies);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = Merge(schoolCookies, ReadSetCookies(refresh));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", schoolHost, refreshed)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", MainHost, refreshed)).StatusCode);
    }

    [Fact]
    public async Task Main_domain_session_is_rejected_on_an_organization_domain()
    {
        var (userName, password, schoolHost) = await CreateSchoolWithOwnerAsync();
        var client = factory.CreateClient(new() { HandleCookies = false });

        var login = await SendAsync(client, HttpMethod.Post, "/api/Auth/login", MainHost, new(), new { userName, password });
        var mainCookies = ReadSetCookies(login);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", MainHost, mainCookies)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", schoolHost, mainCookies)).StatusCode);
    }

    private async Task<(string UserName, string Password, string SchoolHost)> CreateSchoolWithOwnerAsync()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();

        var owner = factory.CreateBrowserClient();
        var userName = (await TestUsers.RegisterAndLoginAsync(owner, TestUsers.Teacher, "hbs")).GetProperty("data").GetProperty("userName").GetString()!;
        var slug = $"school-{Guid.NewGuid():N}"[..20];

        (await admin.PostAsJsonAsync("/api/platform/organizations", new
        {
            name = "Riyadh Model School",
            slug,
            type = "School",
            countryCode = "SA",
            ownerUserName = userName
        })).EnsureSuccessStatusCode();

        return (userName, TestUsers.Password, $"{slug}.localhost");
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string host, Dictionary<string, string> cookies, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}")));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private static Dictionary<string, string> ReadSetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0].Split('=', 2)).Where(p => p[1].Length > 0).ToDictionary(p => p[0], p => p[1])
            : new Dictionary<string, string>();

    private static Dictionary<string, string> Merge(Dictionary<string, string> current, Dictionary<string, string> updates)
    {
        var merged = new Dictionary<string, string>(current);
        foreach (var (key, value) in updates)
            merged[key] = value;
        return merged;
    }
}
