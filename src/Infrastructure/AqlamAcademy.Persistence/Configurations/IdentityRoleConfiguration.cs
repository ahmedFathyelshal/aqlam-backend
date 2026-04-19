using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AqlamAcademy.Infrastructure.Persistence.Constants;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    private static readonly Guid StudentRoleId = Guid.Parse("600455B8-6449-40D4-B9D1-0DFA36633630");
    private static readonly Guid TeacherRoleId = Guid.Parse("96C8E966-E3B2-43E5-BF83-5683707B5061");
    private static readonly Guid AssistantRoleId = Guid.Parse("FAB469E4-D1CD-47E4-B9D1-0DFA36633631");

    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.ToTable("Roles", SchemaNames.Identity);

        builder.HasData(
            new IdentityRole<Guid>
            {
                Id = StudentRoleId,
                Name = "Student",
                NormalizedName = "STUDENT",
                ConcurrencyStamp = "e96ac6c8-eb1a-4712-98b7-6fa3f6db0026"
            },
            new IdentityRole<Guid>
            {
                Id = TeacherRoleId,
                Name = "Teacher",
                NormalizedName = "TEACHER",
                ConcurrencyStamp = "4c5205aa-67af-4b2a-8cfa-5be54edcd163"
            },
            new IdentityRole<Guid>
            {
                Id = AssistantRoleId,
                Name = "Assistant",
                NormalizedName = "ASSISTANT",
                ConcurrencyStamp = "f4fe6bd9-57ef-4e36-a5cc-9c60010c2738"
            }
        );
    }
}
