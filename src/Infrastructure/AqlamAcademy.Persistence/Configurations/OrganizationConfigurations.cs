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
        builder.Property(x => x.DedicatedConnectionString).HasMaxLength(2000);
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
