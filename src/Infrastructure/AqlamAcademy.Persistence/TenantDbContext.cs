using System.Linq.Expressions;
using System.Reflection;
using AqlamAcademy.Domain.Common;
using AqlamAcademy.Domain.Courses;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence;

/// <summary>
/// Tenant store: teaching data owned by one organization. Every entity is marked multi-tenant, so
/// queries are filtered by the current tenant and new rows are stamped with its id automatically.
/// For dedicated-tier tenants the same model lives in a separate database (see DependencyInjection).
/// </summary>
public class TenantDbContext : MultiTenantDbContext
{
    public const string ConfigurationNamespace = "AqlamAcademy.Infrastructure.Persistence.Configurations.Tenant";
    public const string MigrationsHistoryTable = "__TenantMigrationsHistory";
    private const string SoftDeleteFilter = "SoftDelete";

    public TenantDbContext(IMultiTenantContextAccessor multiTenantContextAccessor, DbContextOptions<TenantDbContext> options)
        : base(multiTenantContextAccessor, options)
    {
    }

    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseChapter> CourseChapters => Set<CourseChapter>();
    public DbSet<CourseLesson> CourseLessons => Set<CourseLesson>();
    public DbSet<LessonResource> LessonResources => Set<LessonResource>();
    public DbSet<AqlamAcademy.Domain.Learning.LessonProgress> LessonProgress => Set<AqlamAcademy.Domain.Learning.LessonProgress>();
    public DbSet<AqlamAcademy.Domain.Assessments.Question> Questions => Set<AqlamAcademy.Domain.Assessments.Question>();
    public DbSet<AqlamAcademy.Domain.Assessments.Quiz> Quizzes => Set<AqlamAcademy.Domain.Assessments.Quiz>();
    public DbSet<AqlamAcademy.Domain.Assessments.QuizAttempt> QuizAttempts => Set<AqlamAcademy.Domain.Assessments.QuizAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly(),
            type => type.Namespace == ConfigurationNamespace);

        // Named filter so it composes with the tenant filter instead of replacing it.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(Expression.Property(parameter, nameof(ISoftDelete.IsDeleted)), Expression.Constant(false));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(SoftDeleteFilter, Expression.Lambda(body, parameter));
        }
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ISoftDelete>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAtUtc = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
