using Elaris.Domain.Auth.Enums;
using Elaris.Domain.Auth.Records;

namespace Elaris.Application.Features.Auth.Repositories;

public interface IUserRepository
{
    Task<UserRecord?> FindByPhoneAsync(string phoneE164, CancellationToken cancellationToken);
    Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task<UserRecord?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserRecord> CreateMemberAsync(
        string phoneE164,
        string? email,
        string? password,
        CancellationToken cancellationToken);
    Task MarkPhoneConfirmedAsync(Guid userId, CancellationToken cancellationToken);
    Task MarkEmailConfirmedAsync(Guid userId, CancellationToken cancellationToken);
    Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken);
    Task ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken);
    Task TouchLastLoginAsync(Guid userId, CancellationToken cancellationToken);
    Task SoftDeleteMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task DeactivateMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task ActivateMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task RestrictMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task UnrestrictMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken);
    Task SetPasswordAsync(
        Guid userId,
        string password,
        string? currentPassword,
        CancellationToken cancellationToken);
    Task<PasswordCheckResult> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken);
    Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken);
    Task<bool> AnyAdminExistsAsync(CancellationToken cancellationToken);
    Task<(IReadOnlyList<UserRecord> Items, int TotalCount)> ListAdminsPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken);
    Task<(IReadOnlyList<MemberAdminRecord> Items, int TotalCount)> ListMembersPageAsync(
        int skip,
        int take,
        string? search,
        bool? isActive,
        bool? isRestricted,
        CancellationToken cancellationToken);
    Task<MemberAdminRecord?> FindMemberAdminAsync(Guid userId, CancellationToken cancellationToken);
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);
    Task<int> CountActiveSuperAdminsAsync(CancellationToken cancellationToken);
    Task<UserRecord> CreateAdminAsync(
        string email,
        string? phoneE164,
        string password,
        CancellationToken cancellationToken,
        bool isSuperAdmin = false);
}
