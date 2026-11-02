using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddParentsNotificationsCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Communication");

            migrationBuilder.AddColumn<int>(
                name: "Calendar",
                schema: "Organizations",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Existing Saudi organizations show Hijri and Gregorian side by side (CalendarDisplay.Both = 3).
            migrationBuilder.Sql("UPDATE \"Organizations\".\"Organizations\" SET \"Calendar\" = 3 WHERE \"CountryCode\" = 'SA';");

            migrationBuilder.AddColumn<int>(
                name: "WeekStartDay",
                schema: "Organizations",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0); // Sunday

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "Communication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "Organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParentInvites",
                schema: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentInvites_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "Organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParentInvites_Users_ChildUserId",
                        column: x => x.ChildUserId,
                        principalSchema: "Identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParentLinks",
                schema: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentLinks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "Organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParentLinks_Users_ChildUserId",
                        column: x => x.ChildUserId,
                        principalSchema: "Identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParentLinks_Users_ParentUserId",
                        column: x => x.ParentUserId,
                        principalSchema: "Identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrganizationId",
                schema: "Communication",
                table: "Notifications",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAtUtc",
                schema: "Communication",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvites_ChildUserId",
                schema: "Organizations",
                table: "ParentInvites",
                column: "ChildUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvites_Code",
                schema: "Organizations",
                table: "ParentInvites",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParentInvites_OrganizationId",
                schema: "Organizations",
                table: "ParentInvites",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentLinks_ChildUserId",
                schema: "Organizations",
                table: "ParentLinks",
                column: "ChildUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentLinks_OrganizationId",
                schema: "Organizations",
                table: "ParentLinks",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentLinks_ParentUserId_ChildUserId",
                schema: "Organizations",
                table: "ParentLinks",
                columns: new[] { "ParentUserId", "ChildUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "Communication");

            migrationBuilder.DropTable(
                name: "ParentInvites",
                schema: "Organizations");

            migrationBuilder.DropTable(
                name: "ParentLinks",
                schema: "Organizations");

            migrationBuilder.DropColumn(
                name: "Calendar",
                schema: "Organizations",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "WeekStartDay",
                schema: "Organizations",
                table: "Organizations");
        }
    }
}
