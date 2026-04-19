using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users", SchemaNames.Identity);

        // 1. PhoneNumber: Nullable and Not Unique
        builder.Property(u => u.PhoneNumber)
            .IsRequired(false);

        // 2. Email: Nullable and Not Unique
        builder.Property(u => u.Email)
            .IsRequired(false)
            .HasMaxLength(256);

        builder.HasIndex(u => u.NormalizedEmail).IsUnique(false);

        // 3. UserName: Unique Index
        builder.HasIndex(u => u.NormalizedUserName)
            .HasDatabaseName("UserNameIndex")
            .IsUnique();
    }
}
