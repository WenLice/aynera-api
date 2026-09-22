using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// Two user decisions, both about a stored value matching what the member was shown.
    ///
    /// <para><b>Hometown is required.</b> It has its own page in the app's registration, so a profile
    /// without one is not a shape the product allows any more. Like <c>RequireMemberProfileCity</c>,
    /// this refuses to run rather than invent a value: there is nothing to derive a hometown from,
    /// so a blank row is a question for a person, not a backfill. Fix or remove those rows and rerun.</para>
    ///
    /// <para><b>Third gender is stored as <c>ThirdGender</c>.</b> The enum name was <c>Other</c> while
    /// every client showed "Third Gender / Transgender", so the stored value and the shown value
    /// disagreed. Genders and interested-in choices are both persisted by name, so renaming the enum
    /// member alone would strand existing rows on a value that no longer parses — they are rewritten here.</para>
    /// </summary>
    public partial class RequireHometownAndRenameThirdGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stored by name, so the rows carry the old spelling until they are rewritten.
            migrationBuilder.Sql(
                """
                UPDATE "MemberProfiles"    SET "Gender"       = 'ThirdGender' WHERE "Gender"       = 'Other';
                UPDATE "MemberPreferences" SET "InterestedIn" = 'ThirdGender' WHERE "InterestedIn" = 'Other';
                """);

            // Guard: never let a profile through with a hometown nobody gave. Blank counts as missing,
            // since the column was free text and an empty string is the same absence as NULL.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    missing integer;
                BEGIN
                    SELECT count(*)
                      INTO missing
                      FROM "MemberProfiles"
                     WHERE "Hometown" IS NULL OR btrim("Hometown") = '';

                    IF missing > 0 THEN
                        RAISE EXCEPTION
                            'RequireHometownAndRenameThirdGender: % member profile(s) have no hometown. Hometown is now required and cannot be derived — fill or remove those rows, then rerun the migration.',
                            missing;
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Hometown",
                table: "MemberProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Hometown",
                table: "MemberProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.Sql(
                """
                UPDATE "MemberProfiles"    SET "Gender"       = 'Other' WHERE "Gender"       = 'ThirdGender';
                UPDATE "MemberPreferences" SET "InterestedIn" = 'Other' WHERE "InterestedIn" = 'ThirdGender';
                """);
        }
    }
}
