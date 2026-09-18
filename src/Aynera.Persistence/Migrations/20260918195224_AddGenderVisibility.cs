using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// "Prefer not to say" moves from being a gender to being a property of one: a member states
    /// a gender so the reciprocal hard filter can use it, and chooses separately whether it shows
    /// on their profile.
    ///
    /// Existing members never asked to hide anything, so the column defaults to true. The rows
    /// written while <c>Gender = 'PreferNotToSay'</c> was offered are the exception — they did ask,
    /// so they start hidden. Their stored gender is left alone rather than invented; the enum keeps
    /// that value so those rows still parse, and it is replaced the next time the member saves.
    /// </summary>
    public partial class AddGenderVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GenderIsPublic",
                table: "MemberProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""
                UPDATE "MemberProfiles"
                SET "GenderIsPublic" = FALSE
                WHERE "Gender" = 'PreferNotToSay';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GenderIsPublic",
                table: "MemberProfiles");
        }
    }
}
