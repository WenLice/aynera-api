using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aynera.Persistence.Migrations
{
    /// <summary>
    /// Bangalore opens first: it becomes the only Wave 1 city, with Delhi and Mumbai moved to
    /// Wave 2. Both stay active, so the public site keeps collecting interest for them and the
    /// API keeps resolving them — <c>Wave</c> is metadata clients interpret, not an access gate
    /// (see <c>EarlyAccessCityRepository.FindOpenByNameAsync</c>, which filters on
    /// <c>IsActive</c> only). The member app shows only the Wave 1 cities.
    ///
    /// This is data, not schema. The seeder is insert-only and keyed on name, so it can never
    /// correct rows that already exist — hence a migration. It runs once, so a later deliberate
    /// change via <c>PATCH /early-access/cities/{id}</c> will not be overwritten.
    /// </summary>
    public partial class BangaloreOpensFirst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "EarlyAccessCities" SET "Wave" = 1, "SortOrder" = 1 WHERE "Name" = 'Bangalore';
                UPDATE "EarlyAccessCities" SET "Wave" = 2, "SortOrder" = 2 WHERE "Name" = 'Delhi';
                UPDATE "EarlyAccessCities" SET "Wave" = 2, "SortOrder" = 3 WHERE "Name" = 'Mumbai';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the original seed: all three in Wave 1, Delhi first.
            migrationBuilder.Sql("""
                UPDATE "EarlyAccessCities" SET "Wave" = 1, "SortOrder" = 1 WHERE "Name" = 'Delhi';
                UPDATE "EarlyAccessCities" SET "Wave" = 1, "SortOrder" = 2 WHERE "Name" = 'Bangalore';
                UPDATE "EarlyAccessCities" SET "Wave" = 1, "SortOrder" = 3 WHERE "Name" = 'Mumbai';
                """);
        }
    }
}
