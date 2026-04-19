using AqlamAcademy.Infrastructure.Persistence;

namespace AqlamAcademy.Api.Infrastructure.Extensions;

public static class DatabaseExtensions
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var initializer = scope.ServiceProvider.GetRequiredService<ApplicationDbInitializer>();

        await initializer.InitializeAsync();
        await initializer.SeedAsync();
    }
}
