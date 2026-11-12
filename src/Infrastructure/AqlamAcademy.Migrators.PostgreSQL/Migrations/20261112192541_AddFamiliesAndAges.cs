using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddFamiliesAndAges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BirthYear",
                schema: "Identity",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByUserId",
                schema: "Payments",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganizationId",
                schema: "Organizations",
                table: "ParentLinks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganizationId",
                schema: "Organizations",
                table: "ParentInvites",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthYear",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                schema: "Payments",
                table: "Payments");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganizationId",
                schema: "Organizations",
                table: "ParentLinks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganizationId",
                schema: "Organizations",
                table: "ParentInvites",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
