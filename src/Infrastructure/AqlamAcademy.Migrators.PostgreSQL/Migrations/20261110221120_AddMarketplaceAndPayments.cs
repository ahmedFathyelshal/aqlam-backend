using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqlamAcademy.Migrators.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Payments");

            migrationBuilder.EnsureSchema(
                name: "Marketplace");

            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "Staff",
                table: "TeacherProfiles",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Headline",
                schema: "Staff",
                table: "TeacherProfiles",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsListed",
                schema: "Staff",
                table: "TeacherProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhotoUpdatedAtUtc",
                schema: "Staff",
                table: "TeacherProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicName",
                schema: "Staff",
                table: "TeacherProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPercent",
                schema: "Organizations",
                table: "Organizations",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivationCodeBatches",
                schema: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivationCodeBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivationCodeBatches_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "Organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Courses",
                schema: "Marketplace",
                columns: table => new
                {
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicSlug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ShortCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationSlug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    OrganizationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganizationType = table.Column<int>(type: "integer", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TeacherGender = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    HasPromoVideo = table.Column<bool>(type: "boolean", nullable: false),
                    CurriculumId = table.Column<int>(type: "integer", nullable: true),
                    StageId = table.Column<int>(type: "integer", nullable: true),
                    GradeId = table.Column<int>(type: "integer", nullable: true),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    IsFree = table.Column<bool>(type: "boolean", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CodeActivationOnly = table.Column<bool>(type: "boolean", nullable: false),
                    IsComingSoon = table.Column<bool>(type: "boolean", nullable: false),
                    LessonCount = table.Column<int>(type: "integer", nullable: false),
                    TotalMinutes = table.Column<int>(type: "integer", nullable: false),
                    FreePreviewCount = table.Column<int>(type: "integer", nullable: false),
                    SearchText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ProviderSearchText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.CourseId);
                    table.ForeignKey(
                        name: "FK_Courses_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "Organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeaturedTeachers",
                schema: "Marketplace",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeaturedTeachers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeaturedTeachers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "Organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                schema: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Account = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccessExpiryPolicy = table.Column<int>(type: "integer", nullable: true),
                    AccessDurationDays = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformFees",
                schema: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    MonthlyStudentFeeSar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MonthlyStudentFeeEgp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformFees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSecrets",
                schema: "Payments",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSecrets", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "TeacherPhotos",
                schema: "Staff",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherPhotos", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_TeacherPhotos_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teachers",
                schema: "Marketplace",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Headline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Gender = table.Column<int>(type: "integer", nullable: false),
                    PhotoUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SubjectIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    GradeIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    CurriculumIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    CourseCount = table.Column<int>(type: "integer", nullable: false),
                    SearchText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teachers", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_Teachers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "Organizations",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActivationCodes",
                schema: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastFour = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    RedeemedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RedeemedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivationCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivationCodes_ActivationCodeBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "Payments",
                        principalTable: "ActivationCodeBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivationCodeBatches_OrganizationId_CourseId",
                schema: "Payments",
                table: "ActivationCodeBatches",
                columns: new[] { "OrganizationId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivationCodes_BatchId",
                schema: "Payments",
                table: "ActivationCodes",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivationCodes_CodeHash",
                schema: "Payments",
                table: "ActivationCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_CountryCode_SubjectId_GradeId",
                schema: "Marketplace",
                table: "Courses",
                columns: new[] { "CountryCode", "SubjectId", "GradeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_OrganizationId",
                schema: "Marketplace",
                table: "Courses",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_PublicSlug",
                schema: "Marketplace",
                table: "Courses",
                column: "PublicSlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_ShortCode",
                schema: "Marketplace",
                table: "Courses",
                column: "ShortCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeaturedTeachers_OrganizationId_SubjectId",
                schema: "Marketplace",
                table: "FeaturedTeachers",
                columns: new[] { "OrganizationId", "SubjectId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_OrganizationId_Account",
                schema: "Payments",
                table: "LedgerEntries",
                columns: new[] { "OrganizationId", "Account" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PaymentId_Account",
                schema: "Payments",
                table: "LedgerEntries",
                columns: new[] { "PaymentId", "Account" },
                unique: true,
                filter: "\"PaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TransactionId",
                schema: "Payments",
                table: "LedgerEntries",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrganizationId_CreatedAtUtc",
                schema: "Payments",
                table: "Payments",
                columns: new[] { "OrganizationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Provider_ProviderReference",
                schema: "Payments",
                table: "Payments",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserId_CourseId",
                schema: "Payments",
                table: "Payments",
                columns: new[] { "UserId", "CourseId" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_Slug",
                schema: "Marketplace",
                table: "Teachers",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_TeacherUserId",
                schema: "Marketplace",
                table: "Teachers",
                column: "TeacherUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivationCodes",
                schema: "Payments");

            migrationBuilder.DropTable(
                name: "Courses",
                schema: "Marketplace");

            migrationBuilder.DropTable(
                name: "FeaturedTeachers",
                schema: "Marketplace");

            migrationBuilder.DropTable(
                name: "LedgerEntries",
                schema: "Payments");

            migrationBuilder.DropTable(
                name: "Payments",
                schema: "Payments");

            migrationBuilder.DropTable(
                name: "PlatformFees",
                schema: "Payments");

            migrationBuilder.DropTable(
                name: "PlatformSecrets",
                schema: "Payments");

            migrationBuilder.DropTable(
                name: "TeacherPhotos",
                schema: "Staff");

            migrationBuilder.DropTable(
                name: "Teachers",
                schema: "Marketplace");

            migrationBuilder.DropTable(
                name: "ActivationCodeBatches",
                schema: "Payments");

            migrationBuilder.DropColumn(
                name: "City",
                schema: "Staff",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "Headline",
                schema: "Staff",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "IsListed",
                schema: "Staff",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "PhotoUpdatedAtUtc",
                schema: "Staff",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "PublicName",
                schema: "Staff",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "CommissionPercent",
                schema: "Organizations",
                table: "Organizations");
        }
    }
}
