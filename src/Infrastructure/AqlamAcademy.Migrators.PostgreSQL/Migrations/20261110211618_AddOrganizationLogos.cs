using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationLogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationLogos",
                schema: "Organizations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationLogos", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_OrganizationLogos_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "Organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationLogos",
                schema: "Organizations");
        }
    }
}
