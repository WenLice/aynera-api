using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackMemberId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MemberId",
                table: "FeedbackSubmissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_MemberId",
                table: "FeedbackSubmissions",
                column: "MemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedbackSubmissions_MemberId",
                table: "FeedbackSubmissions");

            migrationBuilder.DropColumn(
                name: "MemberId",
                table: "FeedbackSubmissions");
        }
    }
}
