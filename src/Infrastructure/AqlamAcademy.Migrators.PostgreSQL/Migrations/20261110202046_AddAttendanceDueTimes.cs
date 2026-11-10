using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceDueTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "AttendanceDueBy",
                schema: "Organizations",
                table: "Organizations",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(10, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "GroupAttendanceGraceMinutes",
                schema: "Organizations",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendanceDueBy",
                schema: "Organizations",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "GroupAttendanceGraceMinutes",
                schema: "Organizations",
                table: "Organizations");
        }
    }
}
