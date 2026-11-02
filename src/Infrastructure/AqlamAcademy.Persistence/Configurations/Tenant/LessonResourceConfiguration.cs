using AqlamAcademy.Domain.Courses;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations.Tenant;

public class LessonResourceConfiguration : IEntityTypeConfiguration<LessonResource>
{
    public void Configure(EntityTypeBuilder<LessonResource> builder)
    {
        builder.ToTable("LessonResources", SchemaNames.Courses);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(150);
        builder.Property(x => x.ResourceUrl).IsRequired();

        // The relationship is already configured from the Lesson side, 
        // but it is perfectly fine to configure it here explicitly as well.
        builder.HasOne(x => x.Lesson)
               .WithMany(x => x.Resources)
               .HasForeignKey(x => x.LessonId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
