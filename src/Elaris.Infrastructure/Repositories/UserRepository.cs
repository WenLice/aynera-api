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
        string? password,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("CreateMemberAsync");
        await EnsurePhoneAvailableForRegistrationAsync(phoneE164, cancellationToken);

        if (!string.IsNullOrWhiteSpace(email))
        {
            var byEmail = await _userManager.FindByEmailAsync(email);
            if (byEmail is not null)
            {
                EnsureCanSignIn(byEmail);
                throw new AuthException(
                    "email_already_exists",
                    "A member account with this email already exists.",
                    statusCode: 409);
            }
        }

        return await CreateNewMemberAsync(phoneE164, email, password, cancellationToken);
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

    public async Task RestrictMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("RestrictMemberAsync {UserId}", userId);
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        if (user.IsDeleted)
        {
            throw new AuthException(
                "account_deleted",
                "This account was deleted.",
                statusCode: 409);
        }

        if (user.IsRestricted)
        {
            return;
        }

        user.IsRestricted = true;
        user.RestrictedAtUtc = DateTimeOffset.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new AuthException("user_update_failed", errors, statusCode: 500);
        }
    }

    public async Task UnrestrictMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("UnrestrictMemberAsync {UserId}", userId);
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        if (user.IsDeleted)
        {
            throw new AuthException(
                "account_deleted",
                "This account was deleted.",
                statusCode: 409);
        }

        if (!user.IsRestricted)
        {
            return;
        }

        user.IsRestricted = false;
        user.RestrictedAtUtc = null;

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

        if (existing.IsRestricted)
        {
            throw new AuthException(
                "account_restricted",
                "This account is restricted. Contact support; re-registration is not allowed.",
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
        string? password,
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
            IsRestricted = false,
            IsDeleted = false,
            DeactivatedAtUtc = null,
            RestrictedAtUtc = null,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            EmailConfirmed = false,
            PhoneNumberConfirmed = false,
            TwoFactorEnabled = false,
            LockoutEnabled = true
        };

        var result = string.IsNullOrWhiteSpace(password)
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            ThrowIfPasswordFailed(result);

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

    public async Task MarkEmailConfirmedAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireActiveLifecycleUserAsync(userId);

        user.EmailConfirmed = true;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new AuthException("user_update_failed", errors, statusCode: 500);
        }
    }

    public async Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthException("user_not_found", "Account not found.", statusCode: 404);

        return await _userManager.HasPasswordAsync(user);
    }

    public async Task SetPasswordAsync(
        Guid userId,
        string password,
        string? currentPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireActiveLifecycleUserAsync(userId);

        if (await _userManager.HasPasswordAsync(user))
        {
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                throw new AuthException(
                    "password_current_required",
                    "Current password is required to change your password.",
                    statusCode: 400);
            }

            var change = await _userManager.ChangePasswordAsync(user, currentPassword, password);
            if (!change.Succeeded)
            {
                if (change.Errors.Any(e =>
                    string.Equals(e.Code, "PasswordMismatch", StringComparison.Ordinal)))
                {
                    throw new AuthException(
                        "password_invalid",
                        "Current password is incorrect.",
                        statusCode: 401);
                }

                ThrowIfPasswordFailed(change);
                var errors = string.Join("; ", change.Errors.Select(e => e.Description));
                throw new AuthException("user_update_failed", errors, statusCode: 500);
            }

            return;
        }

        var add = await _userManager.AddPasswordAsync(user, password);
        if (!add.Succeeded)
        {
            ThrowIfPasswordFailed(add);
            var errors = string.Join("; ", add.Errors.Select(e => e.Description));
            throw new AuthException("user_update_failed", errors, statusCode: 500);
        }
    }

    public async Task<PasswordCheckResult> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireActiveLifecycleUserAsync(userId);

        if (await _userManager.IsLockedOutAsync(user))
        {
            return PasswordCheckResult.LockedOut;
        }

        if (!await _userManager.HasPasswordAsync(user))
        {
            return PasswordCheckResult.NotSet;
        }

        if (!await _userManager.CheckPasswordAsync(user, password))
        {
            await _userManager.AccessFailedAsync(user);
            if (await _userManager.IsLockedOutAsync(user))
            {
                return PasswordCheckResult.LockedOut;
            }

            return PasswordCheckResult.Invalid;
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        return PasswordCheckResult.Success;
    }

    public async Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireActiveLifecycleUserAsync(userId);

        if (await _userManager.HasPasswordAsync(user))
        {
            var removed = await _userManager.RemovePasswordAsync(user);
            if (!removed.Succeeded)
            {
                var errors = string.Join("; ", removed.Errors.Select(e => e.Description));
                throw new AuthException("user_update_failed", errors, statusCode: 500);
            }
        }

        var added = await _userManager.AddPasswordAsync(user, newPassword);
        if (!added.Succeeded)
        {
            ThrowIfPasswordFailed(added);
            var errors = string.Join("; ", added.Errors.Select(e => e.Description));
            throw new AuthException("user_update_failed", errors, statusCode: 500);
        }

        await _userManager.ResetAccessFailedCountAsync(user);
    }

    public async Task<bool> AnyAdminExistsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _userManager.Users.AnyAsync(
            u => u.AccountKind == AccountKind.Admin && !u.IsDeleted,
            cancellationToken);
    }

    public async Task<(IReadOnlyList<UserRecord> Items, int TotalCount)> ListAdminsPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = _userManager.Users.AsNoTracking()
            .Where(u => u.AccountKind == AccountKind.Admin);
        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderBy(u => u.Email)
            .ThenBy(u => u.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var items = new List<UserRecord>(entities.Count);
        foreach (var entity in entities)
        {
            items.Add(await ToRecordAsync(entity));
        }

        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<MemberAdminRecord> Items, int TotalCount)> ListMembersPageAsync(
        int skip,
        int take,
        string? search,
        bool? isActive,
        bool? isRestricted,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = _userManager.Users.AsNoTracking()
            .Include(u => u.Profile)
            .Where(u => u.AccountKind == AccountKind.Member);

        if (isActive is not null)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        if (isRestricted is not null)
        {
            query = query.Where(u => u.IsRestricted == isRestricted.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(term))
                || (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(term))
                || (u.Profile != null && u.Profile.FirstName.ToLower().Contains(term))
                || (u.Profile != null && u.Profile.LastName.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .ThenBy(u => u.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var items = entities.Select(ToMemberAdmin).ToList();
        return (items, totalCount);
    }

    public async Task<MemberAdminRecord?> FindMemberAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await _userManager.Users.AsNoTracking()
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(
                u => u.Id == userId && u.AccountKind == AccountKind.Member,
                cancellationToken);
        return entity is null ? null : ToMemberAdmin(entity);
    }

    private static MemberAdminRecord ToMemberAdmin(AppUser entity) =>
        new(
            entity.Id,
            entity.PhoneNumber,
            entity.PhoneNumberConfirmed,
            entity.Email,
            entity.EmailConfirmed,
            entity.IsActive,
            entity.IsRestricted,
            entity.CreatedAtUtc,
            entity.Profile?.FirstName,
            entity.Profile?.LastName,
            entity.Profile?.Gender.ToString(),
            entity.Profile?.DateOfBirth,
            entity.Profile?.City,
            entity.Profile?.Religion);

    public async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _userManager.Users.CountAsync(
            u => u.AccountKind == AccountKind.Admin && u.IsActive,
            cancellationToken);
    }

    public async Task<int> CountActiveSuperAdminsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _userManager.Users.CountAsync(
            u => u.AccountKind == AccountKind.Admin && u.IsActive && u.IsSuperAdmin,
            cancellationToken);
    }

    public async Task<UserRecord> CreateAdminAsync(
        string email,
        string? phoneE164,
        string password,
        CancellationToken cancellationToken,
        bool isSuperAdmin = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("CreateAdminAsync");

        if (!string.IsNullOrWhiteSpace(phoneE164))
        {
            var byPhone = await FindByPhoneAsync(phoneE164, cancellationToken);
            if (byPhone is not null)
            {
                throw new AuthException(
                    "user_already_exists",
                    "An account with this phone already exists.",
                    statusCode: 409);
            }
        }

        var byEmail = await _userManager.FindByEmailAsync(email);
        if (byEmail is not null)
        {
            throw new AuthException(
                "email_already_exists",
                "An account with this email already exists.",
                statusCode: 409);
        }

        var userName = string.IsNullOrWhiteSpace(phoneE164) ? email : phoneE164;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PhoneNumber = string.IsNullOrWhiteSpace(phoneE164) ? null : phoneE164,
            Email = email,
            AccountKind = AccountKind.Admin,
            IsSuperAdmin = isSuperAdmin,
            IsActive = true,
            IsRestricted = false,
            IsDeleted = false,
            DeactivatedAtUtc = null,
            RestrictedAtUtc = null,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            EmailConfirmed = true,
            PhoneNumberConfirmed = !string.IsNullOrWhiteSpace(phoneE164),
            TwoFactorEnabled = false,
            LockoutEnabled = true
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            ThrowIfPasswordFailed(result);
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new AuthException("user_create_failed", errors, statusCode: 500);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, AuthRoles.Admin);
        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            throw new AuthException("role_assign_failed", errors, statusCode: 500);
        }

        return await ToRecordAsync(user);
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

        EnsureCanSignIn(user);
        return user;
    }

    private static void EnsureCanSignIn(AppUser user)
    {
        if (user.IsRestricted)
        {
            throw new AuthException(
                "account_restricted",
                "This account is restricted. Contact support.",
                statusCode: 403);
        }

        if (!user.IsActive)
        {
            throw new AuthException(
                "account_deactivated",
                "This account is deactivated. Activate it before continuing.",
                statusCode: 403);
        }
    }

    private static void EnsureCanSignIn(UserRecord user)
    {
        if (user.IsRestricted)
        {
            throw new AuthException(
                "account_restricted",
                "This account is restricted. Contact support.",
                statusCode: 403);
        }

        if (!user.IsActive)
        {
            throw new AuthException(
                "account_deactivated",
                "This account is deactivated. Activate it before continuing.",
                statusCode: 403);
        }
    }

    private static void ThrowIfPasswordFailed(IdentityResult result)
    {
        if (result.Succeeded || !result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)))
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(e => e.Description));
        throw new AuthException("password_too_weak", errors, statusCode: 400);
    }

    private async Task<UserRecord> ToRecordAsync(AppUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return _mapper.Map<UserRecord>(user) with { Roles = roles.ToList() };
    }
}
