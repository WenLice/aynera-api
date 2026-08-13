using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elaris.Persistence;

public sealed class ElarisDbContextFactory : IDesignTimeDbContextFactory<ElarisDbContext>
{
    public ElarisDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ELARIS_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=elaris;Username=elaris;Password=elaris";

        var options = new DbContextOptionsBuilder<ElarisDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ElarisDbContext(options);
    }
}
