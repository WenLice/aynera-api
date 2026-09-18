using Aynera.Domain.Auth.Enums;
using Aynera.Persistence;
using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// Exercises the <c>RequireMemberProfileCity</c> migration against legacy rows: the schema is stepped
/// back to the nullable column, free-text profiles are inserted, and the migration is replayed.
/// Runs inside the sequential Integration collection because it alters the shared test schema.
/// </summary>
[Collection("Integration")]
public sealed class MemberProfileCityMigrationTests(AuthApiFactory factory)
{
    private const string NullableColumnMigration = "20260913091510_AddMemberProfileCityId";

    [Fact]
    public async Task RequireMemberProfileCity_LinksLegacyRowsByName_AndRefusesUnmatchedOnes()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AyneraDbContext>();
        var migrator = db.GetService<IMigrator>();
        var delhi = await db.EarlyAccessCities.SingleAsync(c => c.Name == "Delhi");

        var matched = await CreateMemberAsync(sp, "matched");
        var unmatched = await CreateMemberAsync(sp, "unmatched");

        try
        {
            // Step back to the state where CityId exists but is nullable, then insert legacy-shaped rows.
            await migrator.MigrateAsync(NullableColumnMigration);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "MemberProfiles" ("UserId", "FirstName", "LastName", "Gender", "DateOfBirth", "City", "CityId", "CreatedAtUtc", "IsDeleted")
                VALUES ({0}, 'Legacy', 'Match', 'Female', '1990-05-15', '  delhi ', NULL, now(), FALSE),
                       ({1}, 'Legacy', 'Miss',  'Female', '1990-05-15', 'Atlantis', NULL, now(), FALSE);
                """,
                matched, unmatched);

            // 1. An unmatched profile must block the migration rather than receive a placeholder city.
            var blocked = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
            Assert.Contains("RequireMemberProfileCity", blocked.MessageText);
            Assert.Contains("Atlantis", blocked.MessageText);

            // 2. Once the unmatched row is gone, the migration links and normalises the remaining rows.
            await db.Database.ExecuteSqlRawAsync("""DELETE FROM "MemberProfiles" WHERE "UserId" = {0}""", unmatched);
            await migrator.MigrateAsync();

            var row = await db.MemberProfiles.AsNoTracking().SingleAsync(p => p.UserId == matched);
            Assert.Equal(delhi.Id, row.CityId);
            Assert.Equal("Delhi", row.City);

            var nullable = await db.Database
                .SqlQueryRaw<string>("""SELECT is_nullable AS "Value" FROM information_schema.columns WHERE table_name = 'MemberProfiles' AND column_name = 'CityId'""")
                .SingleAsync();
            Assert.Equal("NO", nullable);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""DELETE FROM "MemberProfiles" WHERE "UserId" IN ({0}, {1})""", matched, unmatched);
            await migrator.MigrateAsync();
        }
    }

    private static async Task<Guid> CreateMemberAsync(IServiceProvider sp, string tag)
    {
        var phone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = phone,
            PhoneNumber = phone,
            Email = $"city-migration-{tag}-{Guid.NewGuid():N}@example.test",
            AccountKind = AccountKind.Member
        };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        return user.Id;
    }
}
