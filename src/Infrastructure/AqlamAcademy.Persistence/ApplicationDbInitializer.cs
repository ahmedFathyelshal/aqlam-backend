using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Infrastructure.Persistence;

public class ApplicationDbInitializer
{
    private readonly ILogger<ApplicationDbInitializer> _logger;
    private readonly ApplicationDbContext _context;
    private readonly TenantDbContext _tenantContext;

    public ApplicationDbInitializer(ILogger<ApplicationDbInitializer> logger, ApplicationDbContext context, TenantDbContext tenantContext)
    {
        _logger = logger;
        _context = context;
        _tenantContext = tenantContext;
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
        await SeedDataFromJsonAsync("education_stages.json", _context.EducationStages);
        await SeedDataFromJsonAsync("education_grades.json", _context.EducationGrades);
        await SeedDataFromJsonAsync("subjects.json", _context.Subjects);
        await SeedDataFromJsonAsync("course_states.json", _context.CourseStates);
    }

    private async Task SeedDataFromJsonAsync<TEntity>(string fileName, DbSet<TEntity> dbSet) where TEntity : class
    {
        if (await dbSet.AnyAsync()) return; // Already seeded

        // Assuming SeedData is copied to output directory
        var filePath = Path.Combine(AppContext.BaseDirectory, "SeedData", fileName);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Seed data file not found: {FilePath}", filePath);
            return;
        }

        var jsonData = await File.ReadAllTextAsync(filePath);
        var items = System.Text.Json.JsonSerializer.Deserialize<List<TEntity>>(jsonData);

        if (items != null && items.Any())
        {
            // For Lookups where we force Identity Insert (integer PKs)
            var connection = _context.Database.GetDbConnection();
            var strategy = _context.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // PostgreSQL accepts explicit ids for identity columns
                        await dbSet.AddRangeAsync(items);
                        await _context.SaveChangesAsync();
                    
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to seed data for {EntityName}", typeof(TEntity).Name);
                    await transaction.RollbackAsync();
                }
            });
        }
    }
}
