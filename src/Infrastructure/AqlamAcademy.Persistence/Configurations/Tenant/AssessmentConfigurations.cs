using AqlamAcademy.Domain.Assessments;
using AqlamAcademy.Infrastructure.Persistence.Constants;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqlamAcademy.Infrastructure.Persistence.Configurations.Tenant;

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.ToTable("Questions", SchemaNames.Assessments);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Text).IsRequired().HasMaxLength(4000);
        builder.Property(x => x.Explanation).HasMaxLength(4000);
        builder.Property(x => x.Points).HasColumnType("decimal(9,2)");

        // Options travel with the question; stored as JSON.
        builder.OwnsMany(x => x.Options, options => options.ToJson());

        builder.HasIndex(x => new { x.SubjectId, x.GradeId });
    }
}

public class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> builder)
    {
        builder.ToTable("Quizzes", SchemaNames.Assessments);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.LessonId).IsUnique();

        builder.HasMany(x => x.Questions)
               .WithOne()
               .HasForeignKey(x => x.QuizId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuizQuestionConfiguration : IEntityTypeConfiguration<QuizQuestion>
{
    public void Configure(EntityTypeBuilder<QuizQuestion> builder)
    {
        builder.ToTable("QuizQuestions", SchemaNames.Assessments);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Points).HasColumnType("decimal(9,2)");
        builder.HasIndex(x => new { x.QuizId, x.QuestionId }).IsUnique();

        builder.HasOne(x => x.Question)
               .WithMany()
               .HasForeignKey(x => x.QuestionId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> builder)
    {
        builder.ToTable("QuizAttempts", SchemaNames.Assessments);
        builder.IsMultiTenant();
        builder.Property<string>("TenantId").HasMaxLength(64);
        builder.HasIndex("TenantId");

        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.IsSubmitted);
        builder.Property(x => x.Score).HasColumnType("decimal(9,2)");
        builder.Property(x => x.MaxScore).HasColumnType("decimal(9,2)");
        builder.Property(x => x.Percent).HasColumnType("decimal(5,2)");
        builder.HasIndex(x => new { x.UserId, x.QuizId });

        // Frozen question snapshots and answers, stored as JSON with the attempt.
        builder.OwnsMany(x => x.Items, items =>
        {
            items.ToJson();
            items.OwnsMany(i => i.Options);
        });
    }
}
