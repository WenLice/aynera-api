using AutoMapper;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Domain.Auth.Records;
using Elaris.Domain.Auth.Enums;
using Elaris.Domain.Auth.Exceptions;
using Elaris.Domain.Auth.Statics;
using Elaris.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elaris.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IMapper _mapper;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(UserManager<AppUser> userManager, IMapper mapper, ILogger<UserRepository> logger)
    {
        _userManager = userManager;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<UserRecord?> FindByPhoneAsync(string phoneE164, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.UserName == phoneE164, cancellationToken);
        return user is null ? null : await ToRecordAsync(user);
    }

    public async Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var user = await _userManager.FindByEmailAsync(email.Trim());
        return user is null ? null : await ToRecordAsync(user);
    }

    public async Task<UserRecord?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToRecordAsync(user);
    }

    public async Task<UserRecord> CreateMemberAsync(
        string phoneE164,
        string? email,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("CreateMemberAsync");
        await EnsurePhoneAvailableForRegistrationAsync(phoneE164, cancellationToken);

        if (!string.IsNullOrWhiteSpace(email))
        {
            var byEmail = await _userManager.FindByEmailAsync(email);
            if (byEmail is not null)
            {
                EnsureIsActive(byEmail);
                throw new AuthException(
                    "email_already_exists",
                    "A member account with this email already exists.",
                    statusCode: 409);
            }
        }

        return await CreateNewMemberAsync(phoneE164, email, cancellationToken);
    }

    public async Task SoftDeleteMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("SoftDeleteMemberAsync {UserId}", userId);
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        if (user.IsDeleted)
        {
            return;
        }

        await _userManager.UpdateSecurityStampAsync(user);

        var now = DateTimeOffset.UtcNow;
        var stamp = user.Id.ToString("N");
        var phone = user.PhoneNumber ?? user.UserName;

        user.DeletedPhoneE164 = phone;
        user.IsDeleted = true;
        user.IsActive = false;
        user.DeactivatedAtUtc ??= now;
        user.DeletedAtUtc = now;
        user.PhoneNumberConfirmed = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;

        // Free unique UserName / Email so the same phone can register a brand-new account.
        user.UserName = $"{phone}__del__{stamp}";
        user.PhoneNumber = $"{phone}__del__{stamp}";
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            user.Email = $"{stamp}.{user.Email}";
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            _logger.LogError("SoftDeleteMemberAsync failed for {UserId}: {Errors}", userId, errors);
            throw new AuthException("user_delete_failed", errors, statusCode: 500);
        }
    }

    public async Task DeactivateMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("DeactivateMemberAsync {UserId}", userId);
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireActiveLifecycleUserAsync(userId);

        user.IsActive = false;
        user.DeactivatedAtUtc = DateTimeOffset.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new AuthException("user_update_failed", errors, statusCode: 500);
        }
    }

    public async Task ActivateMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("ActivateMemberAsync {UserId}", userId);
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        if (user.IsDeleted)
        {
            throw new AuthException(
                "account_deleted",
                "This account was deleted. Register again to create a new account.",
                statusCode: 409);
        }

        if (user.IsActive)
        {
            return;
        }

        user.IsActive = true;
        user.DeactivatedAtUtc = null;
        user.LockoutEnd = null;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new AuthException("user_update_failed", errors, statusCode: 500);
        }
    }

    private async Task EnsurePhoneAvailableForRegistrationAsync(string phoneE164, CancellationToken cancellationToken)
    {
        var existing = await FindByPhoneAsync(phoneE164, cancellationToken);
        if (existing is null)
        {
            return;
        }

        if (!existing.IsActive)
        {
            throw new AuthException(
                "account_deactivated",
                "This account is deactivated. Activate it before signing in; re-registration is not allowed.",
                statusCode: 409);
        }

        throw new AuthException(
            "user_already_exists",
            "A member account with this phone already exists. Sign in with the existing account.",
            statusCode: 409);
    }

    private async Task<UserRecord> CreateNewMemberAsync(
        string phoneE164,
        string? email,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = phoneE164,
            PhoneNumber = phoneE164,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            AccountKind = AccountKind.Member,
            IsActive = true,
            IsDeleted = false,
            DeactivatedAtUtc = null,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            EmailConfirmed = false,
            PhoneNumberConfirmed = false,
            TwoFactorEnabled = false,
            LockoutEnabled = true
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            var raced = await FindByPhoneAsync(phoneE164, cancellationToken);
            if (raced is not null)
            {
                await EnsurePhoneAvailableForRegistrationAsync(phoneE164, cancellationToken);
            }

            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new AuthException("user_create_failed", errors, statusCode: 500);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, AuthRoles.Member);
        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            throw new AuthException("role_assign_failed", errors, statusCode: 500);
        }

        return await ToRecordAsync(user);
    }

    public async Task MarkPhoneConfirmedAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireActiveLifecycleUserAsync(userId);

        user.PhoneNumberConfirmed = true;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new AuthException("user_update_failed", errors, statusCode: 500);
        }
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        if (user.IsDeleted)
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new AuthException("email_missing", "Account has no email to confirm.", statusCode: 400);
        }

        return await _userManager.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        if (user.IsDeleted)
        {
            throw new AuthException("user_not_found", "Account not found.", statusCode: 404);
        }

        if (user.EmailConfirmed)
        {
            return;
        }

        var result = await _userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            throw new AuthException(
                "email_token_invalid",
                "Email verification link is invalid or expired.",
                statusCode: 400);
        }
    }

    public async Task TouchLastLoginAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireActiveLifecycleUserAsync(userId);

        user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        await _userManager.UpdateAsync(user);
    }

    private async Task<AppUser> RequireActiveLifecycleUserAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        EnsureIsActive(user);
        return user;
    }

    private static void EnsureIsActive(AppUser user)
    {
        if (!user.IsActive)
        {
            throw new AuthException(
                "account_deactivated",
                "This account is deactivated. Activate it before continuing.",
                statusCode: 403);
        }
    }

    private static void EnsureIsActive(UserRecord user)
    {
        if (!user.IsActive)
        {
            throw new AuthException(
                "account_deactivated",
                "This account is deactivated. Activate it before continuing.",
                statusCode: 403);
        }
    }

    private async Task<UserRecord> ToRecordAsync(AppUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return _mapper.Map<UserRecord>(user) with { Roles = roles.ToList() };
    }
}
