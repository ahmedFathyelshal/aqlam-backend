using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class FixIdentityRoleSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "Identity",
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("600455b8-6449-40d4-b9d1-0dfa36633630"),
                column: "ConcurrencyStamp",
                value: "e96ac6c8-eb1a-4712-98b7-6fa3f6db0026");

            migrationBuilder.UpdateData(
                schema: "Identity",
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("96c8e966-e3b2-43e5-bf83-5683707b5061"),
                column: "ConcurrencyStamp",
                value: "4c5205aa-67af-4b2a-8cfa-5be54edcd163");

            migrationBuilder.UpdateData(
                schema: "Identity",
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("fab469e4-d1cd-47e4-b9d1-0dfa36633631"),
                column: "ConcurrencyStamp",
                value: "f4fe6bd9-57ef-4e36-a5cc-9c60010c2738");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "Identity",
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("600455b8-6449-40d4-b9d1-0dfa36633630"),
                column: "ConcurrencyStamp",
                value: "aa86f477-9fb0-4d66-8af5-2b6136f44e2e");

            migrationBuilder.UpdateData(
                schema: "Identity",
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("96c8e966-e3b2-43e5-bf83-5683707b5061"),
                column: "ConcurrencyStamp",
                value: "389e1d57-8834-4a84-909d-128cf469e6c0");

            migrationBuilder.UpdateData(
                schema: "Identity",
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("fab469e4-d1cd-47e4-b9d1-0dfa36633631"),
                column: "ConcurrencyStamp",
                value: "396cf6c1-41ed-4dbf-a4c2-2a27c3eaf832");
        }
    }
}
