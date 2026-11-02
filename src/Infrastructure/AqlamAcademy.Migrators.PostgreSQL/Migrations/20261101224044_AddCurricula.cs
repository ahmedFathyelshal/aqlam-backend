using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddCurricula : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurriculumId",
                schema: "Lookups",
                table: "EducationStages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Curricula",
                schema: "Lookups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Curricula", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EducationStages_CurriculumId",
                schema: "Lookups",
                table: "EducationStages",
                column: "CurriculumId");

            migrationBuilder.CreateIndex(
                name: "IX_Curricula_Code",
                schema: "Lookups",
                table: "Curricula",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EducationStages_Curricula_CurriculumId",
                schema: "Lookups",
                table: "EducationStages",
                column: "CurriculumId",
                principalSchema: "Lookups",
                principalTable: "Curricula",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EducationStages_Curricula_CurriculumId",
                schema: "Lookups",
                table: "EducationStages");

            migrationBuilder.DropTable(
                name: "Curricula",
                schema: "Lookups");

            migrationBuilder.DropIndex(
                name: "IX_EducationStages_CurriculumId",
                schema: "Lookups",
                table: "EducationStages");

            migrationBuilder.DropColumn(
                name: "CurriculumId",
                schema: "Lookups",
                table: "EducationStages");
        }
    }
}
