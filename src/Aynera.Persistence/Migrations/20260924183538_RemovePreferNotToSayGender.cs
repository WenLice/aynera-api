using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// Gender is only Male, Female or ThirdGender now; the old fourth value is gone from the code, so
    /// no stored row may still carry it. (This is about gender only — "Prefer not to say" remains a
    /// valid answer to the everyday and belief questions.)
    /// <para>
    /// A registration draft still holding it only loses that answer — the member is asked their
    /// gender again on the next resume. A saved profile is different: there is no honest way to
    /// pick a gender for someone, so the migration stops and names the count instead, the same rule
    /// the required-hometown and city migrations follow.
    /// </para>
    /// </summary>
    public partial class RemovePreferNotToSayGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "MemberRegistrationDrafts"
                SET "Data" = "Data" - 'gender'
                WHERE "Data"->>'gender' = 'PreferNotToSay';

                DO $$
                DECLARE legacy integer;
                BEGIN
                    SELECT count(*) INTO legacy FROM "MemberProfiles" WHERE "Gender" = 'PreferNotToSay';
                    IF legacy > 0 THEN
                        RAISE EXCEPTION 'RemovePreferNotToSayGender: % member profile(s) still have Gender = PreferNotToSay. Set each to Male, Female or ThirdGender (and hide it via MemberSettings.Visibility if wanted), then migrate again.', legacy;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: no schema changed, and a removed draft answer is simply asked again.
        }
    }
}
