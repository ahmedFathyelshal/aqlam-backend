using AqlamAcademy.Domain.Courses;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations.Tenant;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("Courses", SchemaNames.Courses);

        // Adds the TenantId shadow property and the tenant query filter.
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Slug).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Subtitle).HasMaxLength(500);
        builder.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
        builder.Property(x => x.PromoVideoUrl).HasMaxLength(1000);

        builder.Property(x => x.Price).HasColumnType("decimal(18,2)");

        // Map Enum to integer Status column
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Visibility).HasConversion<int>();

        builder.Property(x => x.CreatedByUserId).IsRequired();

        // Stage/Grade/Subject reference platform lookups: no foreign keys across stores
        // (a dedicated tenant database does not contain the lookup tables).
        builder.HasIndex(x => new { x.StageId, x.GradeId, x.SubjectId });
    }
}
