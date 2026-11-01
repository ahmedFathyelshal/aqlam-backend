using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace AqlamAcademy.Api.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container. Startup applies migrations and seeds lookups.
/// Settings are passed as environment variables because Program reads them eagerly while building services.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        using var rsa = RSA.Create(2048);

        Environment.SetEnvironmentVariable("DatabaseSettings__ConnectionString", _db.GetConnectionString());
        Environment.SetEnvironmentVariable("JwtSettings__Rsa__PrivateKeyXml", rsa.ToXmlString(includePrivateParameters: true));
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitsPerMinute", "1000");
        Environment.SetEnvironmentVariable("JwtSettings__Rsa__PublicKeyXml", rsa.ToXmlString(includePrivateParameters: false));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: auth cookies are not marked Secure, so they work over the in-memory HTTP test server.
        builder.UseEnvironment("Development");
    }

    public HttpClient CreateBrowserClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        AllowAutoRedirect = false
    });

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
