using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using AqlamAcademy.Infrastructure.Persistence.Services;
using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace AqlamAcademy.Infrastructure.Persistence;

public static class DependencyInjection
{
    public const string MigrationsAssembly = "AqlamAcademy.Migrators.PostgreSQL";

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Database
        var databaseSettings = configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() 
            ?? throw new InvalidOperationException("Database settings are not configured.");

        // PostgreSQL only. Platform store (identity, organizations, lookups) always uses the shared database.
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                databaseSettings.ConnectionString,
                x => x.MigrationsAssembly(MigrationsAssembly)));

        // Tenant store: shared database by default; a dedicated-tier tenant uses its own connection string.
        services.AddDbContext<TenantDbContext>((serviceProvider, options) =>
        {
            var tenant = serviceProvider.GetService<IMultiTenantContextAccessor<AqlamTenantInfo>>()?.MultiTenantContext?.TenantInfo;
            var connectionString = string.IsNullOrWhiteSpace(tenant?.ConnectionString)
                ? databaseSettings.ConnectionString
                : tenant.ConnectionString;

            options.UseNpgsql(
                connectionString,
                x => x.MigrationsAssembly(MigrationsAssembly)
                      .MigrationsHistoryTable(TenantDbContext.MigrationsHistoryTable));
        });

        services.Configure<CellSettings>(configuration.GetSection(CellSettings.SectionName));
        services.Configure<PlatformSettings>(configuration.GetSection(PlatformSettings.SectionName));

        // Data Protection keys in the platform database: encrypted values (e.g. dedicated-tier connection strings)
        // can be read by every API instance and survive redeployments.
        services.AddDataProtection()
            .SetApplicationName("AqlamAcademy")
            .PersistKeysToDbContext<ApplicationDbContext>();
        services.AddSingleton<ConnectionStringProtector>();
        services.AddScoped<TenantDatabaseMigrator>();

        // 2. ASP.NET Identity
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 6;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.User.RequireUniqueEmail = false;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // 3. Application Services
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ApplicationDbInitializer>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddMemoryCache();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<PersonalOrganizationFactory>();
        services.AddScoped<IOrganizationAdminService, OrganizationAdminService>();
        services.AddScoped<IOrganizationMembersService, OrganizationMembersService>();
        services.AddScoped<AqlamAcademy.Application.Features.Organizations.Members.IMemberImportService, MemberImportService>();
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        return services;
    }
}
