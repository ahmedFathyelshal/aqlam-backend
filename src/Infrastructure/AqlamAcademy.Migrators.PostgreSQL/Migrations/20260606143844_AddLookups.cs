using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EducationStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationStages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EducationGrades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    EducationStageId = table.Column<int>(type: "integer", nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationGrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EducationGrades_EducationStages_EducationStageId",
                        column: x => x.EducationStageId,
                        principalTable: "EducationStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "EducationStages",
                columns: new[] { "Id", "IsActive", "NameAr", "NameEn", "OrderIndex" },
                values: new object[,]
                {
                    { 1, true, "الابتدائية", "Primary", 1 },
                    { 2, true, "الإعدادية", "Middle", 2 },
                    { 3, true, "الثانوية", "High School", 3 }
                });

            migrationBuilder.InsertData(
                table: "EducationGrades",
                columns: new[] { "Id", "EducationStageId", "IsActive", "NameAr", "NameEn", "OrderIndex" },
                values: new object[,]
                {
                    { 1, 1, true, "الصف الأول", "Grade 1", 1 },
                    { 2, 1, true, "الصف الثاني", "Grade 2", 2 },
                    { 3, 1, true, "الصف الثالث", "Grade 3", 3 },
                    { 4, 1, true, "الصف الرابع", "Grade 4", 4 },
                    { 5, 1, true, "الصف الخامس", "Grade 5", 5 },
                    { 6, 1, true, "الصف السادس", "Grade 6", 6 },
                    { 7, 2, true, "الصف الأول الإعدادي", "Grade 7", 7 },
                    { 8, 2, true, "الصف الثاني الإعدادي", "Grade 8", 8 },
                    { 9, 2, true, "الصف الثالث الإعدادي", "Grade 9", 9 },
                    { 10, 3, true, "الصف الأول الثانوي", "Grade 10", 10 },
                    { 11, 3, true, "الصف الثاني الثانوي", "Grade 11", 11 },
                    { 12, 3, true, "الصف الثالث الثانوي", "Grade 12", 12 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EducationGrades_EducationStageId",
                table: "EducationGrades",
                column: "EducationStageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EducationGrades");

            migrationBuilder.DropTable(
                name: "EducationStages");
        }
    }
}
