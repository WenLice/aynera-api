using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// Widens <c>MaxAge</c> to nullable, where null means an open upper end ("45 and older").
    /// Before this, nobody could express interest above <c>PreferenceRules.AgeMax</c>, so any
    /// member older than that plus flexibility passed nobody's reciprocal age filter and was
    /// guaranteed zero introductions.
    /// </summary>
    public partial class OpenEndedMaxAge : Migration
    {
        /// <summary>The slider ceiling, and the closest honest value for an open end on rollback.</summary>
        private const int ClosedUpperEnd = 45;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lossless: every existing value is kept, the column simply admits null as well.
            migrationBuilder.AlterColumn<int>(
                name: "MaxAge",
                table: "MemberPreferences",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The scaffold defaulted nulls to 0, which would read as "accepts nobody" — the exact
            // dead end this migration exists to remove. Close the range at the old ceiling instead:
            // still lossy, because "and older" cannot be expressed once the column is NOT NULL,
            // but it leaves a member matchable rather than silently unmatchable.
            migrationBuilder.Sql($"""
                UPDATE "MemberPreferences"
                SET "MaxAge" = {ClosedUpperEnd}
                WHERE "MaxAge" IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "MaxAge",
                table: "MemberPreferences",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
