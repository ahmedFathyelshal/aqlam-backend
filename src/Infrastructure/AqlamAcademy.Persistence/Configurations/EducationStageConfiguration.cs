using AqlamAcademy.Domain.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class EducationStageConfiguration : IEntityTypeConfiguration<EducationStage>
{
    public void Configure(EntityTypeBuilder<EducationStage> builder)
    {
        builder.ToTable("EducationStages", SchemaNames.Lookups);

        builder.HasKey(x => x.Id);
        
        // Disable auto-increment for lookup IDs to keep them stable and explicitly seeded
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.NameEn).IsRequired().HasMaxLength(100);
        builder.Property(x => x.NameAr).IsRequired().HasMaxLength(100);

    }
}
