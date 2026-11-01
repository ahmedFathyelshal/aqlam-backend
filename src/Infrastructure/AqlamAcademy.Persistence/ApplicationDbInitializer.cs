using System.Text.Json;
using AqlamAcademy.Domain.Lookups;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Services;
using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Infrastructure.Persistence;

public class ApplicationDbInitializer
{
    private readonly ILogger<ApplicationDbInitializer> _logger;
    private readonly ApplicationDbContext _context;
    private readonly TenantDbContext _tenantContext;
    private readonly PersonalOrganizationFactory _personalOrganizations;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PlatformSettings _platform;
    private readonly TenantDatabaseMigrator _tenantDatabases;

    public ApplicationDbInitializer(ILogger<ApplicationDbInitializer> logger, ApplicationDbContext context, TenantDbContext tenantContext, PersonalOrganizationFactory personalOrganizations,
        UserManager<ApplicationUser> userManager, IOptions<PlatformSettings> platformSettings, TenantDatabaseMigrator tenantDatabases)
    {
        _logger = logger;
        _context = context;
        _tenantContext = tenantContext;
        _personalOrganizations = personalOrganizations;
        _userManager = userManager;
        _platform = platformSettings.Value;
        _tenantDatabases = tenantDatabases;
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (_context.Database.IsRelational())
            {
                await _context.Database.MigrateAsync();

                // Tenant tables in the shared database, then every dedicated-tier database.
                await _tenantContext.Database.MigrateAsync();
                await _tenantDatabases.MigrateAllDedicatedAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initializing the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        // Order matters: stages reference curricula, grades reference stages.
        await UpsertLookupsFromJsonAsync("curricula.json", _context.Curricula);
        await UpsertLookupsFromJsonAsync("education_stages.json", _context.EducationStages);
        await UpsertLookupsFromJsonAsync("education_grades.json", _context.EducationGrades);
        await UpsertLookupsFromJsonAsync("subjects.json", _context.Subjects);
        await UpsertLookupsFromJsonAsync("course_states.json", _context.CourseStates);

        await BackfillPersonalOrganizationsAsync();
        await EnsureBootstrapAdminAsync();
    }

    /// <summary>
    /// Lookups are reference data owned by the platform: rows from the seed file are inserted when missing and
    /// updated when changed, so edits to the JSON reach existing databases. Rows not in the file are left alone.
    /// </summary>
    private async Task UpsertLookupsFromJsonAsync<TEntity>(string fileName, DbSet<TEntity> dbSet) where TEntity : LookupEntity
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "SeedData", fileName);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Seed data file not found: {FilePath}", filePath);
            return;
        }

        var items = JsonSerializer.Deserialize<List<TEntity>>(await File.ReadAllTextAsync(filePath));
        if (items is null || items.Count == 0)
            return;

        var existing = await dbSet.ToDictionaryAsync(e => e.Id);
        foreach (var item in items)
        {
            if (existing.TryGetValue(item.Id, out var current))
                _context.Entry(current).CurrentValues.SetValues(item);
            else
                dbSet.Add(item);
        }

        await _context.SaveChangesAsync();
    }

    private async Task EnsureBootstrapAdminAsync()
    {
        var admin = _platform.BootstrapAdmin;
        if (admin is null || string.IsNullOrWhiteSpace(admin.UserName) || string.IsNullOrWhiteSpace(admin.Password))
            return;

        var user = await _userManager.FindByNameAsync(admin.UserName);
        if (user is null)
        {
            user = new ApplicationUser { UserName = admin.UserName, FullName = "Platform Administrator", EmailConfirmed = true };
            var created = await _userManager.CreateAsync(user, admin.Password);
            if (!created.Succeeded)
            {
                _logger.LogError("Could not create bootstrap admin {UserName}: {Errors}", admin.UserName, string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }
            _logger.LogInformation("Created bootstrap platform administrator {UserName}.", admin.UserName);
        }

        if (!await _userManager.IsInRoleAsync(user, PlatformRoles.SuperAdmin))
            await _userManager.AddToRoleAsync(user, PlatformRoles.SuperAdmin);
    }

    /// <summary>
    /// Teachers registered before organizations existed get their personal organization. Idempotent.
    /// </summary>
    private async Task BackfillPersonalOrganizationsAsync()
    {
        var teacherRoleId = await _context.Roles.Where(r => r.Name == "Teacher").Select(r => r.Id).FirstOrDefaultAsync();
        if (teacherRoleId == Guid.Empty)
            return;

        var teachersWithoutOrganization = await _context.Users
            .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == teacherRoleId))
            .Where(u => !_context.Memberships.Any(m => m.UserId == u.Id && (m.Roles & OrganizationRole.Owner) == OrganizationRole.Owner))
            .ToListAsync();

        foreach (var teacher in teachersWithoutOrganization)
            await _personalOrganizations.AddForAsync(teacher);

        if (teachersWithoutOrganization.Count > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created personal organizations for {Count} existing teachers.", teachersWithoutOrganization.Count);
        }
    }
}
