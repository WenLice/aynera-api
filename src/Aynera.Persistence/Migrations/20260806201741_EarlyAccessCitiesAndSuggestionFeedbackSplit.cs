using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EarlyAccessCitiesAndSuggestionFeedbackSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Suggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DeactivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suggestions", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "Suggestions" ("Id", "FullName", "Email", "Message", "ClientIp", "UserAgent", "IsActive", "IsDeleted", "CreatedAtUtc")
                SELECT "Id", "FullName", "Email", "Message", "ClientIp", "UserAgent", TRUE, FALSE, "CreatedAtUtc"
                FROM "PublicFeedback";
                """);

            migrationBuilder.DropTable(
                name: "PublicFeedback");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAtUtc",
                table: "EarlyAccessSignups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "EarlyAccessSignups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "EarlyAccessSignups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "EarlyAccessSignups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EarlyAccessCities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Wave = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DeactivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EarlyAccessCities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DeactivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackSubmissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessSignups_IsActive",
                table: "EarlyAccessSignups",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessSignups_IsDeleted",
                table: "EarlyAccessSignups",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessCities_IsActive",
                table: "EarlyAccessCities",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessCities_IsDeleted",
                table: "EarlyAccessCities",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessCities_Name",
                table: "EarlyAccessCities",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessCities_SortOrder",
                table: "EarlyAccessCities",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessCities_Wave",
                table: "EarlyAccessCities",
                column: "Wave");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_CreatedAtUtc",
                table: "FeedbackSubmissions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_Email",
                table: "FeedbackSubmissions",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_IsActive",
                table: "FeedbackSubmissions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_IsDeleted",
                table: "FeedbackSubmissions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_CreatedAtUtc",
                table: "Suggestions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_Email",
                table: "Suggestions",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_IsActive",
                table: "Suggestions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Suggestions_IsDeleted",
                table: "Suggestions",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EarlyAccessCities");

            migrationBuilder.DropTable(
                name: "FeedbackSubmissions");

            migrationBuilder.DropTable(
                name: "Suggestions");

            migrationBuilder.DropIndex(
                name: "IX_EarlyAccessSignups_IsActive",
                table: "EarlyAccessSignups");

            migrationBuilder.DropIndex(
                name: "IX_EarlyAccessSignups_IsDeleted",
                table: "EarlyAccessSignups");

            migrationBuilder.DropColumn(
                name: "DeactivatedAtUtc",
                table: "EarlyAccessSignups");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "EarlyAccessSignups");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "EarlyAccessSignups");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "EarlyAccessSignups");

            migrationBuilder.CreateTable(
                name: "PublicFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Topic = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicFeedback", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicFeedback_CreatedAtUtc",
                table: "PublicFeedback",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PublicFeedback_Email",
                table: "PublicFeedback",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_PublicFeedback_Topic",
                table: "PublicFeedback",
                column: "Topic");
        }
    }
}
