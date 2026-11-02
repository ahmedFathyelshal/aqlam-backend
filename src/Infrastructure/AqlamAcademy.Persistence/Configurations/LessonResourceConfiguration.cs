using AqlamAcademy.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class LessonResourceConfiguration : IEntityTypeConfiguration<LessonResource>
{
    public void Configure(EntityTypeBuilder<LessonResource> builder)
    {
        builder.ToTable("LessonResources", SchemaNames.Courses);

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
