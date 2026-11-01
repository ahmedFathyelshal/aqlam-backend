using AqlamAcademy.Domain.Learning;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations.Tenant;

public class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> builder)
    {
        builder.ToTable("LessonProgress", SchemaNames.Learning);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.IsCompleted);

        builder.HasIndex(x => new { x.UserId, x.LessonId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CourseId });
    }
}
