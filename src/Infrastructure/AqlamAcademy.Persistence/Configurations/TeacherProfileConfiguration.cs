using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AqlamAcademy.Domain.Staff;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using AqlamAcademy.Infrastructure.Persistence.Identity;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class TeacherProfileConfiguration : IEntityTypeConfiguration<TeacherProfile>
{
    public void Configure(EntityTypeBuilder<TeacherProfile> builder)
    {
        builder.ToTable("TeacherProfiles", SchemaNames.Staff);

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Bio)
            .HasMaxLength(2000);

        builder.Property(t => t.Specialization)
            .HasMaxLength(250);

        // 1:1 Relationship with ApplicationUser (navigation lives in EF only, not on the domain entity)
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<TeacherProfile>(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

