using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AddClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Classes");

            migrationBuilder.CreateTable(
                name: "Branches",
                schema: "Classes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Gender = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassGroups",
                schema: "Classes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Gender = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurriculumId = table.Column<int>(type: "integer", nullable: true),
                    GradeId = table.Column<int>(type: "integer", nullable: true),
                    HomeroomTeacherUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    Schedule = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassGroups_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "Classes",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassStudents",
                schema: "Classes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassStudents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassStudents_ClassGroups_ClassGroupId",
                        column: x => x.ClassGroupId,
                        principalSchema: "Classes",
                        principalTable: "ClassGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeachingAssignments",
                schema: "Classes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    SubjectName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeachingAssignments_ClassGroups_ClassGroupId",
                        column: x => x.ClassGroupId,
                        principalSchema: "Classes",
                        principalTable: "ClassGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_TenantId",
                schema: "Classes",
                table: "Branches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassGroups_BranchId",
                schema: "Classes",
                table: "ClassGroups",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassGroups_HomeroomTeacherUserId",
                schema: "Classes",
                table: "ClassGroups",
                column: "HomeroomTeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassGroups_TenantId",
                schema: "Classes",
                table: "ClassGroups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassStudents_ClassGroupId_UserId",
                schema: "Classes",
                table: "ClassStudents",
                columns: new[] { "ClassGroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassStudents_TenantId",
                schema: "Classes",
                table: "ClassStudents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassStudents_UserId",
                schema: "Classes",
                table: "ClassStudents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeachingAssignments_ClassGroupId_SubjectName",
                schema: "Classes",
                table: "TeachingAssignments",
                columns: new[] { "ClassGroupId", "SubjectName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeachingAssignments_CourseId",
                schema: "Classes",
                table: "TeachingAssignments",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TeachingAssignments_TeacherUserId",
                schema: "Classes",
                table: "TeachingAssignments",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeachingAssignments_TenantId",
                schema: "Classes",
                table: "TeachingAssignments",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassStudents",
                schema: "Classes");

            migrationBuilder.DropTable(
                name: "TeachingAssignments",
                schema: "Classes");

            migrationBuilder.DropTable(
                name: "ClassGroups",
                schema: "Classes");

            migrationBuilder.DropTable(
                name: "Branches",
                schema: "Classes");
        }
    }
}
