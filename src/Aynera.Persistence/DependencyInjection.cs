using Aynera.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aynera.Persistence;

public sealed class PersistenceOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

public static class DependencyInjection
{
    public static IServiceCollection AddAyneraPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PersistenceOptions>? configure = null)
    {
        var options = new PersistenceOptions
        {
            ConnectionString =
                configuration["AYNERA_DB_CONNECTION"]
                ?? configuration.GetConnectionString("Aynera")
                ?? string.Empty
        };
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Database connection string missing. Set AYNERA_DB_CONNECTION or ConnectionStrings:Aynera.");
        }

        services.AddDbContext<AyneraDbContext>(db => db.UseNpgsql(options.ConnectionString));

        services
            .AddIdentityCore<AppUser>(identity =>
            {
                identity.User.RequireUniqueEmail = false;
                identity.SignIn.RequireConfirmedAccount = false;
                identity.Password.RequiredLength = 8;
                identity.Password.RequireDigit = true;
                identity.Password.RequireLowercase = true;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Password.RequiredUniqueChars = 1;
                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = 10;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AyneraDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }
}
