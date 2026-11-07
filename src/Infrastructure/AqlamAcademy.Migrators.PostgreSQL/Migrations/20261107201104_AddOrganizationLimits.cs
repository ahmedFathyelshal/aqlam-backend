using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AchievementPercent",
                schema: "Organizations",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 85);

            migrationBuilder.AddColumn<int>(
                name: "FollowUpAbsentDays",
                schema: "Organizations",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "FollowUpLateDays",
                schema: "Organizations",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "GoodGradePercent",
                schema: "Organizations",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 75);

            migrationBuilder.AddColumn<int>(
                name: "LowGradePercent",
                schema: "Organizations",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AchievementPercent",
                schema: "Organizations",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "FollowUpAbsentDays",
                schema: "Organizations",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "FollowUpLateDays",
                schema: "Organizations",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "GoodGradePercent",
                schema: "Organizations",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "LowGradePercent",
                schema: "Organizations",
                table: "Organizations");
        }
    }
}
