using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletAndPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MinimumPayoutEgp",
                schema: "Payments",
                table: "PlatformFees",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumPayoutSar",
                schema: "Payments",
                table: "PlatformFees",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayoutHoldDays",
                schema: "Payments",
                table: "PlatformFees",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<Guid>(
                name: "PayoutId",
                schema: "Payments",
                table: "LedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayoutAccounts",
                schema: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountHolder = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BankName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Iban = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayoutAccounts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "Organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payouts",
                schema: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AccountHolder = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BankName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Iban = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransferReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payouts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PayoutId",
                schema: "Payments",
                table: "LedgerEntries",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutAccounts_OrganizationId",
                schema: "Payments",
                table: "PayoutAccounts",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_OrganizationId_Currency",
                schema: "Payments",
                table: "Payouts",
                columns: new[] { "OrganizationId", "Currency" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_OrganizationId_RequestedAtUtc",
                schema: "Payments",
                table: "Payouts",
                columns: new[] { "OrganizationId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_Status_RequestedAtUtc",
                schema: "Payments",
                table: "Payouts",
                columns: new[] { "Status", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayoutAccounts",
                schema: "Payments");

            migrationBuilder.DropTable(
                name: "Payouts",
                schema: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_PayoutId",
                schema: "Payments",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "MinimumPayoutEgp",
                schema: "Payments",
                table: "PlatformFees");

            migrationBuilder.DropColumn(
                name: "MinimumPayoutSar",
                schema: "Payments",
                table: "PlatformFees");

            migrationBuilder.DropColumn(
                name: "PayoutHoldDays",
                schema: "Payments",
                table: "PlatformFees");

            migrationBuilder.DropColumn(
                name: "PayoutId",
                schema: "Payments",
                table: "LedgerEntries");
        }
    }
}
