using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_UserId",
                schema: "Identity",
                table: "UserRefreshTokens");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_ReplacedByTokenId",
                schema: "Identity",
                table: "UserRefreshTokens",
                column: "ReplacedByTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_TokenHash",
                schema: "Identity",
                table: "UserRefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_UserId_Status",
                schema: "Identity",
                table: "UserRefreshTokens",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_ReplacedByTokenId",
                schema: "Identity",
                table: "UserRefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_TokenHash",
                schema: "Identity",
                table: "UserRefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_UserId_Status",
                schema: "Identity",
                table: "UserRefreshTokens");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_UserId",
                schema: "Identity",
                table: "UserRefreshTokens",
                column: "UserId");
        }
    }
}
