using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// Withdraws every city above Wave 1 from the catalog. The pilot is Bangalore only, and
    /// offering a city the product cannot yet serve collects signups nobody will honour.
    ///
    /// Soft-delete rather than DELETE, deliberately: it is the catalog's own removal mechanism
    /// (the <c>!IsDeleted</c> query filter hides the rows from every read, including
    /// <c>FindOpenByNameAsync</c>, so they are gone as far as the product is concerned), it
    /// keeps any historical reference resolvable, and it is reversible when the next city
    /// actually opens. <c>EarlyAccessSignups</c> store the city as a name string and are
    /// untouched, so existing waitlist interest is preserved.
    /// </summary>
    public partial class WithdrawWaveTwoCities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse rather than orphan: MemberProfile.CityId is required and has no FK, so
            // withdrawing a city a member lives in would leave that profile pointing at a row
            // nothing can resolve. Same principle as RequireMemberProfileCity.
            migrationBuilder.Sql("""
                DO $$
                DECLARE stranded int;
                BEGIN
                    SELECT count(*) INTO stranded
                    FROM "MemberProfiles" p
                    JOIN "EarlyAccessCities" c ON c."Id" = p."CityId"
                    WHERE c."Wave" > 1 AND NOT c."IsDeleted";

                    IF stranded > 0 THEN
                        RAISE EXCEPTION
                            'WithdrawWaveTwoCities: % member profile(s) live in a city above Wave 1. Move them before withdrawing it.',
                            stranded;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                UPDATE "EarlyAccessCities"
                SET "IsDeleted" = TRUE,
                    "DeletedAtUtc" = now() AT TIME ZONE 'utc',
                    "IsActive" = FALSE
                WHERE "Wave" > 1 AND NOT "IsDeleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores anything this migration withdrew. It cannot distinguish those rows from a
            // Wave 2+ city an admin deleted by hand, so a manual deletion would come back too.
            migrationBuilder.Sql("""
                UPDATE "EarlyAccessCities"
                SET "IsDeleted" = FALSE,
                    "DeletedAtUtc" = NULL,
                    "IsActive" = TRUE
                WHERE "Wave" > 1 AND "IsDeleted";
                """);
        }
    }
}
