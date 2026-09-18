using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEarlyAccessAndPublicFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EarlyAccessSignups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Interest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsAdult = table.Column<bool>(type: "boolean", nullable: false),
                    MarketingConsent = table.Column<bool>(type: "boolean", nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EarlyAccessSignups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Topic = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicFeedback", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessSignups_City",
                table: "EarlyAccessSignups",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessSignups_CreatedAtUtc",
                table: "EarlyAccessSignups",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyAccessSignups_Email",
                table: "EarlyAccessSignups",
                column: "Email",
                unique: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EarlyAccessSignups");

            migrationBuilder.DropTable(
                name: "PublicFeedback");
        }
    }
}
