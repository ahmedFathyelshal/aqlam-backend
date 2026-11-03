using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class Phase4Academics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Academics");

            migrationBuilder.AddColumn<int>(
                name: "CurriculumId",
                schema: "Classes",
                table: "TeachingAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomSubjectName",
                schema: "Courses",
                table: "Courses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcademicYearId",
                schema: "Classes",
                table: "ClassGroups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcademicYears",
                schema: "Academics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AcademicYears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AcademicTerms",
                schema: "Academics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportCardsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademicTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcademicTerms_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalSchema: "Academics",
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradeCategories",
                schema: "Academics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeachingAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WeightPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeCategories_AcademicTerms_TermId",
                        column: x => x.TermId,
                        principalSchema: "Academics",
                        principalTable: "AcademicTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeCategories_TeachingAssignments_TeachingAssignmentId",
                        column: x => x.TeachingAssignmentId,
                        principalSchema: "Classes",
                        principalTable: "TeachingAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradeItems",
                schema: "Academics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MaxPoints = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceLessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeItems_GradeCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "Academics",
                        principalTable: "GradeCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradeEntries",
                schema: "Academics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeEntries_GradeItems_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "Academics",
                        principalTable: "GradeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademicTerms_AcademicYearId",
                schema: "Academics",
                table: "AcademicTerms",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_AcademicTerms_TenantId",
                schema: "Academics",
                table: "AcademicTerms",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AcademicYears_TenantId",
                schema: "Academics",
                table: "AcademicYears",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeCategories_TeachingAssignmentId_TermId",
                schema: "Academics",
                table: "GradeCategories",
                columns: new[] { "TeachingAssignmentId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeCategories_TenantId",
                schema: "Academics",
                table: "GradeCategories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeCategories_TermId",
                schema: "Academics",
                table: "GradeCategories",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeEntries_ItemId_UserId",
                schema: "Academics",
                table: "GradeEntries",
                columns: new[] { "ItemId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradeEntries_TenantId",
                schema: "Academics",
                table: "GradeEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeItems_CategoryId",
                schema: "Academics",
                table: "GradeItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeItems_TenantId",
                schema: "Academics",
                table: "GradeItems",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradeEntries",
                schema: "Academics");

            migrationBuilder.DropTable(
                name: "GradeItems",
                schema: "Academics");

            migrationBuilder.DropTable(
                name: "GradeCategories",
                schema: "Academics");

            migrationBuilder.DropTable(
                name: "AcademicTerms",
                schema: "Academics");

            migrationBuilder.DropTable(
                name: "AcademicYears",
                schema: "Academics");

            migrationBuilder.DropColumn(
                name: "CurriculumId",
                schema: "Classes",
                table: "TeachingAssignments");

            migrationBuilder.DropColumn(
                name: "CustomSubjectName",
                schema: "Courses",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "AcademicYearId",
                schema: "Classes",
                table: "ClassGroups");
        }
    }
}
