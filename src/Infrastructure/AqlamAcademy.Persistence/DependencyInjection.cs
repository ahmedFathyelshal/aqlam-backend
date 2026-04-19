using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using AqlamAcademy.Infrastructure.Persistence.Services;

namespace AqlamAcademy.Infrastructure.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Database
        var databaseSettings = configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() 
            ?? throw new InvalidOperationException("Database settings are not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            switch (databaseSettings.Provider.ToLowerInvariant())
            {
                case "postgresql":
                case "npgsql":
                    options.UseNpgsql(
                        databaseSettings.ConnectionString,
                        x => x.MigrationsAssembly("AqlamAcademy.Migrators.PostgreSQL"));
                    break;

                case "sqlserver":
                case "mssql":
                    options.UseSqlServer(
                        databaseSettings.ConnectionString,
                        x => x.MigrationsAssembly("AqlamAcademy.Migrators.SqlServer"));
                    break;

                default:
                    throw new InvalidOperationException($"Database provider '{databaseSettings.Provider}' is not supported.");
            }
        });

        // 2. ASP.NET Identity
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;

            options.User.RequireUniqueEmail = false;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // 3. Application Services
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ApplicationDbInitializer>();

        return services;
    }
}

