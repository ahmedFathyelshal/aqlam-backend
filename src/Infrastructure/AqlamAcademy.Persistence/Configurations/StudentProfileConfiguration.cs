using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AqlamAcademy.Domain.Students;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using AqlamAcademy.Infrastructure.Persistence.Identity;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class StudentProfileConfiguration : IEntityTypeConfiguration<StudentProfile>
{
    public void Configure(EntityTypeBuilder<StudentProfile> builder)
    {
        builder.ToTable("StudentProfiles", SchemaNames.Students);

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Grade)
            .HasMaxLength(50);

        builder.Property(s => s.SchoolName)
            .HasMaxLength(250);

        // 1:1 Relationship with ApplicationUser (navigation lives in EF only, not on the domain entity)
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<StudentProfile>(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

