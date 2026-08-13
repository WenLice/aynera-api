using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elaris.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountDeactivatedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_IsActive",
                table: "AspNetUsers",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_IsActive",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DeactivatedAtUtc",
                table: "AspNetUsers");
        }
    }
}
