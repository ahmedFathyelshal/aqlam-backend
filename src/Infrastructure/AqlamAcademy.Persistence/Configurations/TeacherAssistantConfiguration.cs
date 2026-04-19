using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AqlamAcademy.Domain.Staff;
using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class TeacherAssistantConfiguration : IEntityTypeConfiguration<TeacherAssistant>
{
    public void Configure(EntityTypeBuilder<TeacherAssistant> builder)
    {
        builder.ToTable("TeacherAssistants", SchemaNames.Staff);

        // Composite Key
        builder.HasKey(ta => new { ta.TeacherProfileId, ta.AssistantProfileId });

        builder.HasOne(ta => ta.TeacherProfile)
            .WithMany(t => t.Assistants)
            .HasForeignKey(ta => ta.TeacherProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ta => ta.AssistantProfile)
            .WithMany(a => a.Teachers)
            .HasForeignKey(ta => ta.AssistantProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
