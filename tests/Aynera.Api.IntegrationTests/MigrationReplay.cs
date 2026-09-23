using Aynera.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// Puts the shared test database back on the latest migration after a test replayed an old one.
/// <para>
/// Rolling back past <c>MemberProfileBasicDetails</c> drops the Hometown column, which wipes the
/// hometown from every profile the other tests created. Migrating straight to the latest then
/// trips <c>RequireHometownAndRenameThirdGender</c>’s guard, the database is left behind, and
/// every test that runs afterwards fails against the old schema. So: re-add the column, give the
/// wiped fixture rows a placeholder, then finish the climb.
/// </para>
/// </summary>
internal static class MigrationReplay
{
    private const string HometownColumnAdded = "20260918092953_MemberProfileBasicDetails";

    public static async Task RestoreLatestAsync(AyneraDbContext db)
    {
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(HometownColumnAdded);
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE "MemberProfiles" SET "Hometown" = 'Test fixture' WHERE "Hometown" IS NULL OR btrim("Hometown") = ''""");
        await migrator.MigrateAsync();
    }
}
