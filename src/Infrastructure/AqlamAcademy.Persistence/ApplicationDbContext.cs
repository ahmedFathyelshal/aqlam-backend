using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using AqlamAcademy.Domain.Students;
using AqlamAcademy.Domain.Staff;
using AqlamAcademy.Domain.Common;
using AqlamAcademy.Domain.Lookups;
using AqlamAcademy.Domain.Organizations;

namespace AqlamAcademy.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<TeacherProfile> TeacherProfiles => Set<TeacherProfile>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<AssistantProfile> AssistantProfiles => Set<AssistantProfile>();
    public DbSet<TeacherAssistant> TeacherAssistants => Set<TeacherAssistant>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();

    // Lookups
    public DbSet<Curriculum> Curricula => Set<Curriculum>();
    public DbSet<EducationStage> EducationStages => Set<EducationStage>();
    public DbSet<EducationGrade> EducationGrades => Set<EducationGrade>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<AqlamAcademy.Domain.Lookups.CourseState> CourseStates => Set<AqlamAcademy.Domain.Lookups.CourseState>();

    // Organizations (tenants) and memberships
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationDomain> OrganizationDomains => Set<OrganizationDomain>();
    public DbSet<OrganizationLogo> OrganizationLogos => Set<OrganizationLogo>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<JoinCode> JoinCodes => Set<JoinCode>();
    public DbSet<AqlamAcademy.Domain.Learning.Enrollment> Enrollments => Set<AqlamAcademy.Domain.Learning.Enrollment>();
    public DbSet<ParentLink> ParentLinks => Set<ParentLink>();
    public DbSet<ParentInvite> ParentInvites => Set<ParentInvite>();
    public DbSet<AqlamAcademy.Domain.Communication.Notification> Notifications => Set<AqlamAcademy.Domain.Communication.Notification>();

    // Marketplace (public copies of public courses and listed teachers) and money
    public DbSet<AqlamAcademy.Domain.Marketplace.MarketplaceCourse> MarketplaceCourses => Set<AqlamAcademy.Domain.Marketplace.MarketplaceCourse>();
    public DbSet<AqlamAcademy.Domain.Marketplace.MarketplaceTeacher> MarketplaceTeachers => Set<AqlamAcademy.Domain.Marketplace.MarketplaceTeacher>();
    public DbSet<AqlamAcademy.Domain.Marketplace.FeaturedTeacher> FeaturedTeachers => Set<AqlamAcademy.Domain.Marketplace.FeaturedTeacher>();
    public DbSet<TeacherPhoto> TeacherPhotos => Set<TeacherPhoto>();
    public DbSet<AqlamAcademy.Domain.Payments.Payment> Payments => Set<AqlamAcademy.Domain.Payments.Payment>();
    public DbSet<AqlamAcademy.Domain.Payments.LedgerEntry> LedgerEntries => Set<AqlamAcademy.Domain.Payments.LedgerEntry>();
    public DbSet<AqlamAcademy.Domain.Payments.PlatformFees> PlatformFees => Set<AqlamAcademy.Domain.Payments.PlatformFees>();
    public DbSet<AqlamAcademy.Domain.Payments.ActivationCodeBatch> ActivationCodeBatches => Set<AqlamAcademy.Domain.Payments.ActivationCodeBatch>();
    public DbSet<AqlamAcademy.Domain.Payments.ActivationCode> ActivationCodes => Set<AqlamAcademy.Domain.Payments.ActivationCode>();
    public DbSet<AqlamAcademy.Domain.Payments.PlatformSecret> PlatformSecrets => Set<AqlamAcademy.Domain.Payments.PlatformSecret>();

    // ASP.NET Data Protection key ring (shared by all API instances)
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Platform store: identity, organizations, memberships, lookups. Tenant-owned configurations
        // (courses etc.) are excluded here and applied by TenantDbContext.
        // This automatically applies all IEntityTypeConfiguration implementations in the assembly,
        // including our custom Identity, Table renames, and Role seeding configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly(),
            type => type.Namespace != TenantDbContext.ConfigurationNamespace);

        // Configure Global Soft-Delete Filter
        ConfigureSoftDeleteFilter(modelBuilder);
    }

    private static void ConfigureSoftDeleteFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
                var comparison = Expression.Equal(property, Expression.Constant(false));
                var lambda = Expression.Lambda(comparison, parameter);

                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ISoftDelete>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAtUtc = DateTime.UtcNow;
                // entry.Entity.DeletedBy = _currentUserService.UserId; // Placeholder
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

