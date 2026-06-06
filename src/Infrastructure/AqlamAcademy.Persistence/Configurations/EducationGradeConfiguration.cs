using AqlamAcademy.Domain.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class EducationGradeConfiguration : IEntityTypeConfiguration<EducationGrade>
{
    public void Configure(EntityTypeBuilder<EducationGrade> builder)
    {
        builder.ToTable("EducationGrades");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.NameEn).IsRequired().HasMaxLength(100);
        builder.Property(x => x.NameAr).IsRequired().HasMaxLength(100);

        builder.HasOne(x => x.Stage)
            .WithMany(s => s.Grades)
            .HasForeignKey(x => x.EducationStageId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
