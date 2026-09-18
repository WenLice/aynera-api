using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// Replaces FirstName/LastName with a single required Name (the member writes a first name or a
    /// full name, their choice) and adds the rest of the basic details the app collects: an optional
    /// Nickname shown to strangers before a match, plus HeightCm, Hometown and Work.
    /// Existing rows keep their name: Name is backfilled from "FirstName LastName" before the old
    /// columns are dropped, and the migration refuses to run if that leaves any row without one.
    /// </summary>
    public partial class MemberProfileBasicDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "MemberProfiles",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "MemberProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeightCm",
                table: "MemberProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hometown",
                table: "MemberProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Work",
                table: "MemberProfiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Carry each existing member's name over as one value.
            migrationBuilder.Sql("""
                UPDATE "MemberProfiles"
                SET "Name" = left(btrim(coalesce("FirstName", '') || ' ' || coalesce("LastName", '')), 150)
                WHERE "Name" IS NULL;
                """);

            // A member without a name would break the required column and every display path that
            // reads it, so stop here rather than invent one.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    unnamed bigint;
                BEGIN
                    SELECT count(*) INTO unnamed
                    FROM "MemberProfiles"
                    WHERE "Name" IS NULL OR btrim("Name") = '';

                    IF unnamed > 0 THEN
                        RAISE EXCEPTION
                            'MemberProfileBasicDetails: % member profile(s) have no first or last name to build a Name from. Fill them in before migrating.',
                            unnamed;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""ALTER TABLE "MemberProfiles" ALTER COLUMN "Name" SET NOT NULL;""");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "MemberProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "MemberProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "MemberProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Split Name back at the first space. A single-word Name leaves LastName empty, which is
            // the best that can be recovered — the split is lossy by nature.
            migrationBuilder.Sql("""
                UPDATE "MemberProfiles"
                SET "FirstName" = left(split_part(btrim("Name"), ' ', 1), 100),
                    "LastName" = left(btrim(substr(btrim("Name"), length(split_part(btrim("Name"), ' ', 1)) + 1)), 100);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "MemberProfiles" ALTER COLUMN "FirstName" SET NOT NULL;
                ALTER TABLE "MemberProfiles" ALTER COLUMN "LastName" SET NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "Name",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "Hometown",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "Work",
                table: "MemberProfiles");
        }
    }
}
