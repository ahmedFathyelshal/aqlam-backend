using AqlamAcademy.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class CourseLessonConfiguration : IEntityTypeConfiguration<CourseLesson>
{
    public void Configure(EntityTypeBuilder<CourseLesson> builder)
    {
        builder.ToTable("CourseLessons", SchemaNames.Courses);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.VideoUrl).HasMaxLength(1000);
        builder.Property(x => x.Summary).HasMaxLength(500);

        builder.HasOne(x => x.Chapter)
               .WithMany(x => x.Lessons)
               .HasForeignKey(x => x.ChapterId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Resources)
               .WithOne(x => x.Lesson)
               .HasForeignKey(x => x.LessonId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
