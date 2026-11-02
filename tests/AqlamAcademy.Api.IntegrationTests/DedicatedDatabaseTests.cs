using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DedicatedDatabaseTests(ApiFactory factory)
{
    [Fact]
    public async Task Dedicated_school_data_is_stored_only_in_its_own_database_and_the_connection_string_is_encrypted()
    {
        var dedicatedConnectionString = await CreateEmptyDatabaseAsync();
        var admin = await LoginAdminAsync();
        var owner = factory.CreateBrowserClient();
        var ownerName = (await TestUsers.RegisterAndLoginAsync(owner, TestUsers.Teacher, "ded")).GetProperty("data").GetProperty("userName").GetString()!;
        var slug = $"school-{Guid.NewGuid():N}"[..20];

        var create = await admin.PostAsJsonAsync("/api/platform/organizations", new
        {
            name = "Dedicated International School",
            slug,
            type = "School",
            countryCode = "SA",
            ownerUserName = ownerName,
            isolationTier = "Dedicated",
            dedicatedConnectionString
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var org = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("Dedicated", org.GetProperty("isolationTier").GetString());
        var orgId = org.GetProperty("id").GetString()!;

        var draft = await owner.PostAsJsonAsync($"/api/orgs/{slug}/courses/draft", new { title = "Dedicated course" });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);

        Assert.Equal(1, await CountCoursesAsync(dedicatedConnectionString, orgId));
        Assert.Equal(0, await CountCoursesAsync(factory.SharedConnectionString, orgId));

        var stored = await ScalarAsync<string>(factory.SharedConnectionString,
            "SELECT \"DedicatedConnectionString\" FROM \"Organizations\".\"Organizations\" WHERE \"Slug\" = @slug", ("slug", slug));
        Assert.DoesNotContain("Password", stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unreachable_dedicated_database_is_rejected_and_the_organization_is_not_created()
    {
        var admin = await LoginAdminAsync();
        var ownerName = (await TestUsers.RegisterAndLoginAsync(factory.CreateBrowserClient(), TestUsers.Teacher, "dedx"))
            .GetProperty("data").GetProperty("userName").GetString()!;
        var slug = $"school-{Guid.NewGuid():N}"[..20];
        // A server that does not answer (closed port). A missing database on a reachable server is created by the migration.
        var unreachable = new NpgsqlConnectionStringBuilder(factory.SharedConnectionString) { Host = "127.0.0.1", Port = 1, Timeout = 3 };

        var create = await admin.PostAsJsonAsync("/api/platform/organizations", new
        {
            name = "Broken School",
            slug,
            type = "School",
            countryCode = "SA",
            ownerUserName = ownerName,
            isolationTier = "Dedicated",
            dedicatedConnectionString = unreachable.ConnectionString
        });

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        var list = (await admin.GetFromJsonAsync<JsonElement>("/api/platform/organizations")).GetProperty("data");
        Assert.DoesNotContain(list.EnumerateArray(), o => o.GetProperty("slug").GetString() == slug);
    }

    [Fact]
    public async Task Shared_tier_rejects_a_connection_string()
    {
        var admin = await LoginAdminAsync();

        var create = await admin.PostAsJsonAsync("/api/platform/organizations", new
        {
            name = "Shared School",
            slug = $"school-{Guid.NewGuid():N}"[..20],
            type = "School",
            countryCode = "SA",
            ownerUserName = ApiFactory.PlatformAdminUserName,
            dedicatedConnectionString = factory.SharedConnectionString
        });

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    private async Task<HttpClient> LoginAdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();
        return admin;
    }

    private async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = "dedicated_" + Guid.NewGuid().ToString("N")[..10];
        await using (var connection = new NpgsqlConnection(factory.SharedConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(factory.SharedConnectionString) { Database = name }.ConnectionString;
    }

    private static async Task<long> CountCoursesAsync(string connectionString, string organizationId) =>
        await ScalarAsync<long>(connectionString,
            "SELECT COUNT(*) FROM \"Courses\".\"Courses\" WHERE \"TenantId\" = @tenant", ("tenant", organizationId));

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
