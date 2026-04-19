using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AqlamAcademy.Domain.Staff;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using AqlamAcademy.Infrastructure.Persistence.Identity;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class AssistantProfileConfiguration : IEntityTypeConfiguration<AssistantProfile>
{
    public void Configure(EntityTypeBuilder<AssistantProfile> builder)
    {
        builder.ToTable("AssistantProfiles", SchemaNames.Staff);

        builder.HasKey(a => a.Id);

        // 1:1 Relationship with ApplicationUser (navigation lives in EF only, not on the domain entity)
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<AssistantProfile>(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

