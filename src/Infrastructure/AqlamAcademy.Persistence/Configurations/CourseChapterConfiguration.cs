using AqlamAcademy.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class CourseChapterConfiguration : IEntityTypeConfiguration<CourseChapter>
{
    public void Configure(EntityTypeBuilder<CourseChapter> builder)
    {
        builder.ToTable("CourseChapters", SchemaNames.Courses);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired().HasMaxLength(255);

        builder.HasOne(x => x.Course)
               .WithMany(x => x.Chapters)
               .HasForeignKey(x => x.CourseId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
