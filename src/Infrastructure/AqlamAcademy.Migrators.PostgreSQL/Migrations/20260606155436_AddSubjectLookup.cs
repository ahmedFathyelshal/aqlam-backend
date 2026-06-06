using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjectLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Subjects",
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
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subjects");
        }
    }
}
