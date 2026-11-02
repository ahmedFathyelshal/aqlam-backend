using AqlamAcademy.Domain.Attendance;
using AqlamAcademy.Domain.Classes;
using AqlamAcademy.Domain.Communication;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations.Tenant;

public class AttendanceSessionConfiguration : IEntityTypeConfiguration<AttendanceSession>
{
    public void Configure(EntityTypeBuilder<AttendanceSession> builder)
    {
        builder.ToTable("AttendanceSessions", SchemaNames.Classes);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        // One sheet per class, day and meeting time (NULLS NOT DISTINCT: one daily sheet per day).
        builder.HasIndex(x => new { x.ClassGroupId, x.Date, x.StartTime }).IsUnique().AreNullsDistinct(false);

        builder.HasOne<ClassGroup>().WithMany().HasForeignKey(x => x.ClassGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Records).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("AttendanceRecords", SchemaNames.Classes);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasIndex(x => new { x.SessionId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.UserId); // a student's history
    }
}

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("Announcements", SchemaNames.Classes);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(5000);
        builder.Property(x => x.Scope).HasConversion<int>();
        builder.Property(x => x.Audience).HasConversion<int>();
        builder.HasIndex(x => x.PublishedAtUtc);
        builder.HasIndex(x => x.ClassGroupId);
    }
}
