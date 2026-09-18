using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aynera.Persistence;

public sealed class AyneraDbContextFactory : IDesignTimeDbContextFactory<AyneraDbContext>
{
    public AyneraDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AYNERA_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=aynera;Username=aynera;Password=Admin@1";

        var options = new DbContextOptionsBuilder<AyneraDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AyneraDbContext(options);
    }
}
