using System.Text.Json;
using AqlamAcademy.Domain.Lookups;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Infrastructure.Persistence;

public class ApplicationDbInitializer
{
    private readonly ILogger<ApplicationDbInitializer> _logger;
    private readonly ApplicationDbContext _context;
    private readonly TenantDbContext _tenantContext;
    private readonly PersonalOrganizationFactory _personalOrganizations;

    public ApplicationDbInitializer(ILogger<ApplicationDbInitializer> logger, ApplicationDbContext context, TenantDbContext tenantContext, PersonalOrganizationFactory personalOrganizations)
    {
        _logger = logger;
        _context = context;
        _tenantContext = tenantContext;
        _personalOrganizations = personalOrganizations;
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (_context.Database.IsRelational())
            {
                await _context.Database.MigrateAsync();

                // Tenant tables in the shared database. Dedicated-tier databases get their own migration run (P2).
                await _tenantContext.Database.MigrateAsync();
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
