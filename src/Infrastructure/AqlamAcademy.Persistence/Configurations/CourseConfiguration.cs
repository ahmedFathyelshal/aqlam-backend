using AqlamAcademy.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("Courses", SchemaNames.Courses);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Slug).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Subtitle).HasMaxLength(500);
        builder.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
        builder.Property(x => x.PromoVideoUrl).HasMaxLength(1000);

        builder.Property(x => x.Price).HasColumnType("decimal(18,2)");

        // Map Enum to integer Status column
        builder.Property(x => x.Status).HasConversion<int>();

        // Relationship mapping to Lookups
        builder.HasOne(x => x.Stage)
               .WithMany()
               .HasForeignKey(x => x.StageId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Grade)
               .WithMany()
               .HasForeignKey(x => x.GradeId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Subject)
               .WithMany()
               .HasForeignKey(x => x.SubjectId)
               .OnDelete(DeleteBehavior.Restrict);

        // Required Teacher mapping
        builder.Property(x => x.TeacherId).IsRequired();
    }
}
