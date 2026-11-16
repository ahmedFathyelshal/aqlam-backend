using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries", SchemaNames.Organizations);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Details).HasMaxLength(1000);
        builder.HasIndex(x => x.AtUtc);
        builder.HasIndex(x => x.TargetUserId);
        // No foreign keys on purpose: the trail must outlive deleted users and organizations.
    }
}
