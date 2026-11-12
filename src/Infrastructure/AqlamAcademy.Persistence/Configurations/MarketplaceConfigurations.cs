using AqlamAcademy.Domain.Marketplace;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Domain.Payments;
using AqlamAcademy.Domain.Staff;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations;

public class MarketplaceCourseConfiguration : IEntityTypeConfiguration<MarketplaceCourse>
{
    public void Configure(EntityTypeBuilder<MarketplaceCourse> builder)
    {
        builder.ToTable("Courses", SchemaNames.Marketplace);
        builder.HasKey(x => x.CourseId);
        builder.HasIndex(x => x.PublicSlug).IsUnique();
        builder.HasIndex(x => x.ShortCode).IsUnique();
        builder.Property(x => x.ShortCode).HasMaxLength(12).IsRequired();
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => new { x.CountryCode, x.SubjectId, x.GradeId });
        builder.Property(x => x.PublicSlug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.OrganizationSlug).HasMaxLength(63).IsRequired();
        builder.Property(x => x.OrganizationName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.TeacherName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Subtitle).HasMaxLength(500);
        builder.Property(x => x.ThumbnailUrl).HasMaxLength(2048);
        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.Property(x => x.SearchText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ProviderSearchText).HasMaxLength(1000).IsRequired();
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MarketplaceTeacherConfiguration : IEntityTypeConfiguration<MarketplaceTeacher>
{
    public void Configure(EntityTypeBuilder<MarketplaceTeacher> builder)
    {
        builder.ToTable("Teachers", SchemaNames.Marketplace);
        builder.HasKey(x => x.OrganizationId);
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.TeacherUserId);
        builder.Property(x => x.Slug).HasMaxLength(63).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Headline).HasMaxLength(200);
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.PhotoUrl).HasMaxLength(300);
        builder.Property(x => x.SearchText).HasMaxLength(2000).IsRequired();
        builder.HasOne<Organization>().WithOne().HasForeignKey<MarketplaceTeacher>(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class FeaturedTeacherConfiguration : IEntityTypeConfiguration<FeaturedTeacher>
{
    public void Configure(EntityTypeBuilder<FeaturedTeacher> builder)
    {
        builder.ToTable("FeaturedTeachers", SchemaNames.Marketplace);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OrganizationId, x.SubjectId }).IsUnique().AreNullsDistinct(false);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeacherPhotoConfiguration : IEntityTypeConfiguration<TeacherPhoto>
{
    public void Configure(EntityTypeBuilder<TeacherPhoto> builder)
    {
        builder.ToTable("TeacherPhotos", SchemaNames.Staff);
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(32);
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<TeacherPhoto>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", SchemaNames.Payments);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Provider, x.ProviderReference }).IsUnique();
        // At most one open checkout per student and course: a second click reuses it instead of charging twice.
        builder.HasIndex(x => new { x.UserId, x.CourseId }).IsUnique().HasFilter($"\"{nameof(Payment.Status)}\" = {(int)PaymentStatus.Pending}");
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAtUtc });
        builder.Property(x => x.CourseTitle).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(128).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(500);
    }
}

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries", SchemaNames.Payments);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TransactionId);
        builder.HasIndex(x => new { x.OrganizationId, x.Account });
        // A payment is booked once, whatever the provider repeats.
        builder.HasIndex(x => new { x.PaymentId, x.Account }).IsUnique().HasFilter($"\"{nameof(LedgerEntry.PaymentId)}\" IS NOT NULL");
        builder.Property(x => x.Amount).HasPrecision(18, 4);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(300).IsRequired();
    }
}

public class PlatformFeesConfiguration : IEntityTypeConfiguration<PlatformFees>
{
    public void Configure(EntityTypeBuilder<PlatformFees> builder)
    {
        builder.ToTable("PlatformFees", SchemaNames.Payments);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CommissionPercent).HasPrecision(5, 2);
        builder.Property(x => x.MonthlyStudentFeeSar).HasPrecision(18, 2);
        builder.Property(x => x.MonthlyStudentFeeEgp).HasPrecision(18, 2);
    }
}

public class ActivationCodeBatchConfiguration : IEntityTypeConfiguration<ActivationCodeBatch>
{
    public void Configure(EntityTypeBuilder<ActivationCodeBatch> builder)
    {
        builder.ToTable("ActivationCodeBatches", SchemaNames.Payments);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OrganizationId, x.CourseId });
        builder.Property(x => x.CourseTitle).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.HasMany(x => x.Codes).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PlatformSecretConfiguration : IEntityTypeConfiguration<PlatformSecret>
{
    public void Configure(EntityTypeBuilder<PlatformSecret> builder)
    {
        builder.ToTable("PlatformSecrets", SchemaNames.Payments);
        builder.HasKey(x => x.Name);
        builder.Property(x => x.Name).HasMaxLength(64);
        builder.Property(x => x.ProtectedValue).HasMaxLength(2000).IsRequired();
    }
}

public class ActivationCodeConfiguration : IEntityTypeConfiguration<ActivationCode>
{
    public void Configure(EntityTypeBuilder<ActivationCode> builder)
    {
        builder.ToTable("ActivationCodes", SchemaNames.Payments);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.CodeHash).IsUnique();
        builder.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LastFour).HasMaxLength(4).IsRequired();
    }
}
