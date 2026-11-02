using AqlamAcademy.Domain.Organizations;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Infrastructure.Persistence.MultiTenancy;

/// <summary>
/// Encrypts dedicated-tier connection strings at rest (ASP.NET Data Protection; keys live in the platform database
/// so every API instance can decrypt them).
/// </summary>
public class ConnectionStringProtector
{
    private readonly IDataProtector _protector;

    public ConnectionStringProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Aqlam.TenantConnectionStrings.v1");
    }

    public string Protect(string connectionString) => _protector.Protect(connectionString);

    public string Unprotect(string protectedConnectionString) => _protector.Unprotect(protectedConnectionString);
}

/// <summary>Applies tenant migrations to dedicated-tier databases (the shared database is migrated at startup).</summary>
public class TenantDatabaseMigrator
{
    private readonly ApplicationDbContext _platform;
    private readonly ConnectionStringProtector _protector;
    private readonly ILogger<TenantDatabaseMigrator> _logger;

    public TenantDatabaseMigrator(ApplicationDbContext platform, ConnectionStringProtector protector, ILogger<TenantDatabaseMigrator> logger)
    {
        _platform = platform;
        _protector = protector;
        _logger = logger;
    }

    /// <summary>
    /// Creates/updates the tenant schema in the given database. A missing database on a reachable server is
    /// created (the login needs CREATEDB); an unreachable server throws.
    /// </summary>
    public async Task MigrateAsync(Guid organizationId, string slug, string connectionString, CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString, x => x
                .MigrationsAssembly(DependencyInjection.MigrationsAssembly)
                .MigrationsHistoryTable(TenantDbContext.MigrationsHistoryTable))
            .Options;

        var accessor = new StaticMultiTenantContextAccessor<AqlamTenantInfo>(new AqlamTenantInfo
        {
            Id = organizationId.ToString(),
            Identifier = slug,
            IsolationTier = IsolationTier.Dedicated,
            ConnectionString = connectionString
        });

        await using var context = new TenantDbContext(accessor, options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Migrates every dedicated-tier organization. A failing tenant is logged and skipped so one unreachable
    /// school database does not take the whole platform down; its requests fail until it is fixed.
    /// </summary>
    public async Task MigrateAllDedicatedAsync(CancellationToken cancellationToken = default)
    {
        var dedicated = await _platform.Organizations.AsNoTracking()
            .Where(o => o.IsolationTier == IsolationTier.Dedicated && o.DedicatedConnectionString != null)
            .Select(o => new { o.Id, o.Slug, o.DedicatedConnectionString })
            .ToListAsync(cancellationToken);

        foreach (var tenant in dedicated)
        {
            try
            {
                await MigrateAsync(tenant.Id, tenant.Slug, _protector.Unprotect(tenant.DedicatedConnectionString!), cancellationToken);
                _logger.LogInformation("Migrated dedicated database for organization {Slug}.", tenant.Slug);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to migrate dedicated database for organization {Slug}.", tenant.Slug);
            }
        }
    }
}
