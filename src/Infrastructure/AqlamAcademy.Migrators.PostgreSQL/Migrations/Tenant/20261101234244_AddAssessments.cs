using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Assessments");

            migrationBuilder.CreateTable(
                name: "Questions",
                schema: "Assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Points = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    CurriculumId = table.Column<int>(type: "integer", nullable: true),
                    GradeId = table.Column<int>(type: "integer", nullable: true),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    AcceptedAnswers = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    Options = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuizAttempts",
                schema: "Assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuizId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Score = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    AutoSubmitted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    Items = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Quizzes",
                schema: "Assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimeLimitMinutes = table.Column<int>(type: "integer", nullable: true),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: true),
                    PassPercent = table.Column<int>(type: "integer", nullable: false),
                    ShuffleQuestions = table.Column<bool>(type: "boolean", nullable: false),
                    ShuffleOptions = table.Column<bool>(type: "boolean", nullable: false),
                    ShowAnswersAfterSubmit = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quizzes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuizQuestions",
                schema: "Assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuizId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalSchema: "Assessments",
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizQuestions_Quizzes_QuizId",
                        column: x => x.QuizId,
                        principalSchema: "Assessments",
                        principalTable: "Quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SubjectId_GradeId",
                schema: "Assessments",
                table: "Questions",
                columns: new[] { "SubjectId", "GradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_TenantId",
                schema: "Assessments",
                table: "Questions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_TenantId",
                schema: "Assessments",
                table: "QuizAttempts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_UserId_QuizId",
                schema: "Assessments",
                table: "QuizAttempts",
                columns: new[] { "UserId", "QuizId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestions_QuestionId",
                schema: "Assessments",
                table: "QuizQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestions_QuizId_QuestionId",
                schema: "Assessments",
                table: "QuizQuestions",
                columns: new[] { "QuizId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestions_TenantId",
                schema: "Assessments",
                table: "QuizQuestions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_LessonId",
                schema: "Assessments",
                table: "Quizzes",
                column: "LessonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_TenantId",
                schema: "Assessments",
                table: "Quizzes",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuizAttempts",
                schema: "Assessments");

            migrationBuilder.DropTable(
                name: "QuizQuestions",
                schema: "Assessments");

            migrationBuilder.DropTable(
                name: "Questions",
                schema: "Assessments");

            migrationBuilder.DropTable(
                name: "Quizzes",
                schema: "Assessments");
        }
    }
}
