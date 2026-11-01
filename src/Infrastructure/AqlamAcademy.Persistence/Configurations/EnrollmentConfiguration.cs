using AqlamAcademy.Domain.Learning;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("Enrollments", SchemaNames.Learning);
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CourseTitle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Source).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();

        // One enrollment per student and course (re-enrolling reactivates it).
        builder.HasIndex(x => new { x.UserId, x.CourseId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.CourseId });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
