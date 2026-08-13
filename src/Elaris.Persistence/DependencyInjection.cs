using Elaris.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elaris.Persistence;

public sealed class PersistenceOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

public static class DependencyInjection
{
    public static IServiceCollection AddElarisPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PersistenceOptions>? configure = null)
    {
        var options = new PersistenceOptions
        {
            ConnectionString =
                configuration["ELARIS_DB_CONNECTION"]
                ?? configuration.GetConnectionString("Elaris")
                ?? string.Empty
        };
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Database connection string missing. Set ELARIS_DB_CONNECTION or ConnectionStrings:Elaris.");
        }

        services.AddDbContext<ElarisDbContext>(db => db.UseNpgsql(options.ConnectionString));

        services
            .AddIdentityCore<AppUser>(identity =>
            {
                identity.User.RequireUniqueEmail = false;
                identity.SignIn.RequireConfirmedAccount = false;
                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = 10;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ElarisDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }
}
