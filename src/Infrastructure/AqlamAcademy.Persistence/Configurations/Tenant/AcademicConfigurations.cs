using AqlamAcademy.Domain.Academics;
using AqlamAcademy.Domain.Classes;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations.Tenant;

public class AcademicYearConfiguration : IEntityTypeConfiguration<AcademicYear>
{
    public void Configure(EntityTypeBuilder<AcademicYear> builder)
    {
        builder.ToTable("AcademicYears", SchemaNames.Academics);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.HasMany(x => x.Terms).WithOne().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AcademicTermConfiguration : IEntityTypeConfiguration<AcademicTerm>
{
    public void Configure(EntityTypeBuilder<AcademicTerm> builder)
    {
        builder.ToTable("AcademicTerms", SchemaNames.Academics);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
    }
}

public class GradeCategoryConfiguration : IEntityTypeConfiguration<GradeCategory>
{
    public void Configure(EntityTypeBuilder<GradeCategory> builder)
    {
        builder.ToTable("GradeCategories", SchemaNames.Academics);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.WeightPercent).HasColumnType("decimal(5,2)");
        builder.HasIndex(x => new { x.TeachingAssignmentId, x.TermId });

        // Assignments and terms with grades cannot be removed (the services refuse); empty categories go with them.
        builder.HasOne<TeachingAssignment>().WithMany().HasForeignKey(x => x.TeachingAssignmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AcademicTerm>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GradeItemConfiguration : IEntityTypeConfiguration<GradeItem>
{
    public void Configure(EntityTypeBuilder<GradeItem> builder)
    {
        builder.ToTable("GradeItems", SchemaNames.Academics);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.MaxPoints).HasColumnType("decimal(9,2)");
        builder.Property(x => x.Source).HasConversion<int>();
        builder.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GradeEntryConfiguration : IEntityTypeConfiguration<GradeEntry>
{
    public void Configure(EntityTypeBuilder<GradeEntry> builder)
    {
        builder.ToTable("GradeEntries", SchemaNames.Academics);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Score).HasColumnType("decimal(9,2)");
        builder.HasIndex(x => new { x.ItemId, x.UserId }).IsUnique();
    }
}
