using Aynera.Domain.Auth.Statics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aynera.Infrastructure;

public sealed class RoleSeedHostedService : IHostedService
{
    private const string LegacyStaffRole = "staff";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RoleSeedHostedService> _logger;

    public RoleSeedHostedService(IServiceScopeFactory scopeFactory, ILogger<RoleSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        await EnsureRoleAsync(roleManager, AuthRoles.Member, cancellationToken);
        await RenameLegacyStaffRoleAsync(roleManager, cancellationToken);
        await EnsureRoleAsync(roleManager, AuthRoles.Admin, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RenameLegacyStaffRoleAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await roleManager.RoleExistsAsync(AuthRoles.Admin)
            || !await roleManager.RoleExistsAsync(LegacyStaffRole))
        {
            return;
        }

        var staff = await roleManager.FindByNameAsync(LegacyStaffRole);
        if (staff is null)
        {
            return;
        }

        staff.Name = AuthRoles.Admin;
        staff.NormalizedName = roleManager.NormalizeKey(AuthRoles.Admin);
        var result = await roleManager.UpdateAsync(staff);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            _logger.LogError("Failed to rename staff role to admin: {Errors}", errors);
        }
    }

    private async Task EnsureRoleAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        string roleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await roleManager.RoleExistsAsync(roleName))
        {
            return;
        }

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName)
        {
            Id = Guid.NewGuid()
        });

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            _logger.LogError("Failed to seed {Role} role: {Errors}", roleName, errors);
        }
    }
}
