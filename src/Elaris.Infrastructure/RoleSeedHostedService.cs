using Elaris.Domain.Auth.Statics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elaris.Infrastructure;

public sealed class RoleSeedHostedService : IHostedService
{
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

        if (!await roleManager.RoleExistsAsync(AuthRoles.Member))
        {
            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(AuthRoles.Member)
            {
                Id = Guid.NewGuid()
            });

            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                _logger.LogError("Failed to seed member role: {Errors}", errors);
            }
        }

        if (!await roleManager.RoleExistsAsync(AuthRoles.Staff))
        {
            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(AuthRoles.Staff)
            {
                Id = Guid.NewGuid()
            });

            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                _logger.LogError("Failed to seed staff role: {Errors}", errors);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
