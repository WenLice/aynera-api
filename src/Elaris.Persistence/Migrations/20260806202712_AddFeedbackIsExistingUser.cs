using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elaris.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackIsExistingUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExistingUser",
                table: "FeedbackSubmissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_IsExistingUser",
                table: "FeedbackSubmissions",
                column: "IsExistingUser");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedbackSubmissions_IsExistingUser",
                table: "FeedbackSubmissions");

            migrationBuilder.DropColumn(
                name: "IsExistingUser",
                table: "FeedbackSubmissions");
        }
    }
}
