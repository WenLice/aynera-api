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
/// Exercises the <c>MemberProfileBasicDetails</c> migration against rows that still have
/// FirstName/LastName: the schema is stepped back, legacy-shaped profiles are inserted, and the
/// migration is replayed. Runs inside the sequential Integration collection because it alters the
/// shared test schema.
/// </summary>
[Collection("Integration")]
public sealed class MemberProfileNameMigrationTests(AuthApiFactory factory)
{
    /// <summary>The last migration that still had FirstName/LastName.</summary>
    private const string SplitNameMigration = "20260916081640_RequireMemberProfileCity";

    /// <summary>
    /// The migration under test. The test migrates to it, not to the latest: later migrations have
    /// their own guards (a required hometown) that these deliberately old-style rows would trip.
    /// </summary>
    private const string MigrationUnderTest = "20260918092953_MemberProfileBasicDetails";

    [Fact]
    public async Task MemberProfileBasicDetails_JoinsLegacyNames_AndRefusesRowsWithNone()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AyneraDbContext>();
        var migrator = db.GetService<IMigrator>();
        var city = await db.EarlyAccessCities.SingleAsync(c => c.Name == "Bangalore");

        var both = await CreateMemberAsync(sp, "both");
        var firstOnly = await CreateMemberAsync(sp, "first-only");
        var nameless = await CreateMemberAsync(sp, "nameless");

        try
        {
            await migrator.MigrateAsync(SplitNameMigration);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "MemberProfiles" ("UserId", "FirstName", "LastName", "Gender", "DateOfBirth", "City", "CityId", "CreatedAtUtc", "IsDeleted")
                VALUES ({0}, 'Ada',  'Lovelace', 'Female', '1990-05-15', {3}, {4}, now(), FALSE),
                       ({1}, 'Prince', '',       'Other',  '1990-05-15', {3}, {4}, now(), FALSE),
                       ({2}, '',     '  ',       'Male',   '1990-05-15', {3}, {4}, now(), FALSE);
                """,
                both, firstOnly, nameless, city.Name, city.Id);

            // 1. A row with nothing to build a name from must block the migration, not get a placeholder.
            var blocked = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(MigrationUnderTest));
            Assert.Contains("MemberProfileBasicDetails", blocked.MessageText);

            // 2. Once it is gone, both remaining rows keep their name as one value.
            await db.Database.ExecuteSqlRawAsync("""DELETE FROM "MemberProfiles" WHERE "UserId" = {0}""", nameless);
            await migrator.MigrateAsync(MigrationUnderTest);

            // Raw SQL: the schema is at the migration under test, not at today's model.
            Assert.Equal("Ada Lovelace", await ScalarAsync(db, """SELECT "Name" AS "Value" FROM "MemberProfiles" WHERE "UserId" = {0}""", both));
            Assert.Null(await ScalarAsync(db, """SELECT "Nickname" AS "Value" FROM "MemberProfiles" WHERE "UserId" = {0}""", both));

            // A member with no last name is not left with a trailing space.
            Assert.Equal("Prince", await ScalarAsync(db, """SELECT "Name" AS "Value" FROM "MemberProfiles" WHERE "UserId" = {0}""", firstOnly));

            var nullable = await db.Database
                .SqlQueryRaw<string>("""SELECT is_nullable AS "Value" FROM information_schema.columns WHERE table_name = 'MemberProfiles' AND column_name = 'Name'""")
                .SingleAsync();
            Assert.Equal("NO", nullable);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "MemberProfiles" WHERE "UserId" IN ({0}, {1}, {2})""",
                both, firstOnly, nameless);
            await MigrationReplay.RestoreLatestAsync(db);
        }
    }

    private static Task<string?> ScalarAsync(AyneraDbContext db, string sql, Guid userId) =>
        db.Database.SqlQueryRaw<string?>(sql, userId).SingleAsync();

    private static async Task<Guid> CreateMemberAsync(IServiceProvider sp, string tag)
    {
        var phone = "+919" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = phone,
            PhoneNumber = phone,
            Email = $"name-migration-{tag}-{Guid.NewGuid():N}@example.test",
            AccountKind = AccountKind.Member
        };
        var manager = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        return user.Id;
    }
}
