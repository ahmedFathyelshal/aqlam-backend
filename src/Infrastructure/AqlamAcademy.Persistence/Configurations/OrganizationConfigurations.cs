using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations", SchemaNames.Organizations);
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Slug).IsRequired().HasMaxLength(60);
        builder.HasIndex(x => x.Slug).IsUnique();

        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.IsolationTier).HasConversion<int>();
        builder.Property(x => x.PublicPublishing).HasConversion<int>();

        builder.Property(x => x.CountryCode).IsRequired().HasMaxLength(2);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.TimeZone).IsRequired().HasMaxLength(64);
        builder.Property(x => x.LogoUrl).HasMaxLength(1000);
        builder.Property(x => x.PrimaryColor).HasMaxLength(9);
        builder.Property(x => x.ContactPhone).HasMaxLength(32);
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.FollowUpAbsentDays).HasDefaultValue(3);
        builder.Property(x => x.FollowUpLateDays).HasDefaultValue(5);
        builder.Property(x => x.GoodGradePercent).HasDefaultValue(75);
        builder.Property(x => x.LowGradePercent).HasDefaultValue(60);
        builder.Property(x => x.AchievementPercent).HasDefaultValue(85);
        builder.Property(x => x.AttendanceDueBy).HasDefaultValue(new TimeOnly(10, 0));
        builder.Property(x => x.GroupAttendanceGraceMinutes).HasDefaultValue(30);
        builder.Property(x => x.TeachersCreateCourses).HasDefaultValue(false);
        builder.Property(x => x.DedicatedConnectionString).HasMaxLength(4000); // encrypted (Data Protection)
    }
}

public class OrganizationLogoConfiguration : IEntityTypeConfiguration<OrganizationLogo>
{
    public void Configure(EntityTypeBuilder<OrganizationLogo> builder)
    {
        builder.ToTable("OrganizationLogos", SchemaNames.Organizations);
        builder.HasKey(x => x.OrganizationId);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(32);
        builder.HasOne<Organization>().WithOne().HasForeignKey<OrganizationLogo>(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrganizationDomainConfiguration : IEntityTypeConfiguration<OrganizationDomain>
{
    public void Configure(EntityTypeBuilder<OrganizationDomain> builder)
    {
        builder.ToTable("OrganizationDomains", SchemaNames.Organizations);
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Host).IsRequired().HasMaxLength(253);
        builder.HasIndex(x => x.Host).IsUnique();

        builder.HasOne(x => x.Organization)
               .WithMany(o => o.Domains)
               .HasForeignKey(x => x.OrganizationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("Memberships", SchemaNames.Organizations);
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Roles).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Source).HasConversion<int>();

        builder.HasIndex(x => new { x.UserId, x.OrganizationId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.Status });

        builder.HasOne(x => x.Organization)
               .WithMany(o => o.Memberships)
               .HasForeignKey(x => x.OrganizationId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
               .WithMany()
               .HasForeignKey(x => x.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class JoinCodeConfiguration : IEntityTypeConfiguration<JoinCode>
{
    public void Configure(EntityTypeBuilder<JoinCode> builder)
    {
        builder.ToTable("JoinCodes", SchemaNames.Organizations);
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(16);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Role).HasConversion<int>();

        builder.HasOne(x => x.Organization)
               .WithMany()
               .HasForeignKey(x => x.OrganizationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
