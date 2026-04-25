using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    public void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        // Table Name 
        builder.ToTable("UserRefreshTokens", SchemaNames.Identity);

        // Primary Key is handled via [Key] in entity, but this makes it explicit.
        builder.HasKey(rt => rt.Id);

        // Security / Identity overrides
        builder.HasOne(rt => rt.User)
               .WithMany(u => u.RefreshTokens)
               .HasForeignKey(rt => rt.UserId)
               .OnDelete(DeleteBehavior.Cascade);
               
        // Audit logic configuration
        builder.Property(rt => rt.CreatedByIp)
               .HasMaxLength(50);
               
        builder.Property(rt => rt.UserAgent)
               .HasMaxLength(500);
               
        builder.Property(rt => rt.DeviceId)
               .HasMaxLength(100);
               
        builder.Property(rt => rt.ReasonRevoked)
               .HasMaxLength(250);
               
        builder.Property(rt => rt.TokenHash)
               .IsRequired()
               .HasMaxLength(256);
    }
}
