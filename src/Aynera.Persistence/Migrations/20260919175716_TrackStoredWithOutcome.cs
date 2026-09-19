using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// Renames <c>IntentOutcome</c> to <c>Outcome</c> and adds the now-stored <c>Track</c>.
    ///
    /// Hand-written: the scaffold matched the columns alphabetically and renamed
    /// <c>IntentOutcome</c> to <c>Track</c>, then added <c>Outcome</c> with an empty default —
    /// which would have moved values like <c>Prospect</c> into the track column and left every
    /// existing member with no outcome at all. The rename below is explicit and the track is
    /// derived from the outcome that is already there, matching
    /// <c>PreferenceRules.TrackFor</c>: Platonic and Spontaneous are Fluid, the rest Intent.
    /// </summary>
    public partial class TrackStoredWithOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IntentOutcome",
                table: "MemberPreferences",
                newName: "Outcome");

            migrationBuilder.AddColumn<string>(
                name: "Track",
                table: "MemberPreferences",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "MemberPreferences"
                SET "Track" = CASE
                    WHEN "Outcome" IN ('Platonic', 'Spontaneous') THEN 'Fluid'
                    ELSE 'Intent'
                END;
                """);

            // Refuse rather than guess: an unmatched row would mean an outcome this mapping does
            // not know, and inventing a track for it would be the kind of silent corruption this
            // migration exists to avoid.
            migrationBuilder.Sql("""
                DO $$
                DECLARE unmapped int;
                BEGIN
                    SELECT count(*) INTO unmapped FROM "MemberPreferences" WHERE "Track" IS NULL;
                    IF unmapped > 0 THEN
                        RAISE EXCEPTION
                            'TrackStoredWithOutcome: % preference row(s) have an Outcome with no known track.',
                            unmapped;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "MemberPreferences" ALTER COLUMN "Track" SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lossless: the track was always recoverable from the outcome, which is why it was
            // derived before this migration.
            migrationBuilder.DropColumn(
                name: "Track",
                table: "MemberPreferences");

            migrationBuilder.RenameColumn(
                name: "Outcome",
                table: "MemberPreferences",
                newName: "IntentOutcome");
        }
    }
}
