using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// Every member belongs to exactly one catalog city. Profiles created before <c>AddMemberProfileCityId</c>
    /// stored free text only; this migration links them to <c>EarlyAccessCities</c> by name and then makes the
    /// key mandatory. It deliberately refuses to run if any profile cannot be matched rather than inventing a
    /// value: fix or remove those rows, then rerun.
    /// </summary>
    public partial class RequireMemberProfileCity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill: match the stored text to the catalog case-insensitively (trimmed) and normalise the
            // stored name to the catalog spelling. Soft-deleted catalog rows are ignored; closed (inactive)
            // cities still match because the member registered while they were open.
            migrationBuilder.Sql(
                """
                UPDATE "MemberProfiles" AS p
                SET    "CityId" = c."Id",
                       "City"   = c."Name"
                FROM   "EarlyAccessCities" AS c
                WHERE  p."CityId" IS NULL
                  AND  c."IsDeleted" = FALSE
                  AND  lower(btrim(p."City")) = lower(c."Name");
                """);

            // Guard: never let an unmatched profile through with an empty/placeholder city.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    unmatched integer;
                    sample    text;
                BEGIN
                    SELECT count(*), string_agg(DISTINCT "City", ', ')
                      INTO unmatched, sample
                      FROM "MemberProfiles"
                     WHERE "CityId" IS NULL;

                    IF unmatched > 0 THEN
                        RAISE EXCEPTION
                            'RequireMemberProfileCity: % member profile(s) have a City that matches no EarlyAccessCities row (%). Fix or remove them, then rerun the migration.',
                            unmatched, sample;
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CityId",
                table: "MemberProfiles",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CityId",
                table: "MemberProfiles",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
