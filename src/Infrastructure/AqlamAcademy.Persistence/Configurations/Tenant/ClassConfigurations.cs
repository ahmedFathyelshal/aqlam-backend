using AqlamAcademy.Domain.Classes;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations.Tenant;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches", SchemaNames.Classes);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.Gender).HasConversion<int>();
    }
}

public class ClassGroupConfiguration : IEntityTypeConfiguration<ClassGroup>
{
    public void Configure(EntityTypeBuilder<ClassGroup> builder)
    {
        builder.ToTable("ClassGroups", SchemaNames.Classes);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Kind).HasConversion<int>();
        builder.Property(x => x.Gender).HasConversion<int>();
        builder.HasIndex(x => x.HomeroomTeacherUserId);

        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(x => x.Schedule, slots => slots.ToJson());

        builder.HasMany(x => x.Students).WithOne().HasForeignKey(x => x.ClassGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Assignments).WithOne().HasForeignKey(x => x.ClassGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ClassStudentConfiguration : IEntityTypeConfiguration<ClassStudent>
{
    public void Configure(EntityTypeBuilder<ClassStudent> builder)
    {
        builder.ToTable("ClassStudents", SchemaNames.Classes);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ClassGroupId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.UserId); // "my classes" for students and parents
    }
}

public class TeachingAssignmentConfiguration : IEntityTypeConfiguration<TeachingAssignment>
{
    public void Configure(EntityTypeBuilder<TeachingAssignment> builder)
    {
        builder.ToTable("TeachingAssignments", SchemaNames.Classes);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubjectName).IsRequired().HasMaxLength(100);
        builder.HasIndex(x => new { x.ClassGroupId, x.SubjectName }).IsUnique(); // one teacher per subject per class
        builder.HasIndex(x => x.TeacherUserId);
        builder.HasIndex(x => x.CourseId);
    }
}
