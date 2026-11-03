using AqlamAcademy.Domain.Communication;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications", SchemaNames.Communication);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(5000);
        builder.Property(x => x.Link).HasMaxLength(500);

        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ParentLinkConfiguration : IEntityTypeConfiguration<ParentLink>
{
    public void Configure(EntityTypeBuilder<ParentLink> builder)
    {
        builder.ToTable("ParentLinks", SchemaNames.Organizations);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ParentUserId, x.ChildUserId }).IsUnique();
        builder.HasIndex(x => x.ChildUserId);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ParentUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ChildUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ParentInviteConfiguration : IEntityTypeConfiguration<ParentInvite>
{
    public void Configure(EntityTypeBuilder<ParentInvite> builder)
    {
        builder.ToTable("ParentInvites", SchemaNames.Organizations);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(32);
        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ChildUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
