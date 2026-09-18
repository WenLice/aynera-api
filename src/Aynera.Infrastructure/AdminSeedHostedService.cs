using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Audit.Statics;
using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Auth.Statics;
using Aynera.Domain.Common.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Infrastructure;

public sealed class AdminSeedHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AdminSeedOptions> _options;
    private readonly ILogger<AdminSeedHostedService> _logger;

    public AdminSeedHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<AdminSeedOptions> options,
        ILogger<AdminSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seed = _options.Value;
        var email = string.IsNullOrWhiteSpace(seed.Email) ? null : EmailNormalizer.Normalize(seed.Email);
        var password = seed.Password;
        var phoneRaw = string.IsNullOrWhiteSpace(seed.Phone) ? null : seed.Phone.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogInformation("Admin seed skipped (email or password not configured).");
            return;
        }

        if (!RequestValidation.BeValidEmail(email) || !PasswordRules.IsWellFormed(password))
        {
            _logger.LogError("Admin seed skipped because email or password does not meet rules.");
            return;
        }

        string? phoneE164 = null;
        if (phoneRaw is not null)
        {
            try
            {
                phoneE164 = PhoneNormalizer.NormalizeIndianMobile(phoneRaw);
            }
            catch (AuthException)
            {
                _logger.LogError("Admin seed skipped because the configured phone is invalid.");
                return;
            }
        }

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        if (await users.AnyAdminExistsAsync(cancellationToken))
        {
            _logger.LogInformation("Admin seed skipped; an admin account already exists.");
            return;
        }

        try
        {
            var admin = await users.CreateAdminAsync(
                email,
                phoneE164,
                password,
                cancellationToken,
                isSuperAdmin: true);
            _logger.LogInformation(
                "Seeded first admin {UserId} for {Email}",
                admin.Id,
                AuditRedaction.MaskEmail(email));
        }
        catch (AuthException ex)
        {
            _logger.LogError(ex, "Admin seed failed {ErrorCode}", ex.ErrorCode);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
