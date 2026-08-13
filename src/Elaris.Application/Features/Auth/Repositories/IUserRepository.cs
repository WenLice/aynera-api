using Elaris.Domain.Auth.Records;

namespace Elaris.Application.Features.Auth.Repositories;

public interface IUserRepository
{
    Task<UserRecord?> FindByPhoneAsync(string phoneE164, CancellationToken cancellationToken);
    Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task<UserRecord?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserRecord> CreateMemberAsync(string phoneE164, string? email, CancellationToken cancellationToken);
    Task MarkPhoneConfirmedAsync(Guid userId, CancellationToken cancellationToken);
    Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken);
    Task ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken);
    Task TouchLastLoginAsync(Guid userId, CancellationToken cancellationToken);
    Task SoftDeleteMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task DeactivateMemberAsync(Guid userId, CancellationToken cancellationToken);
    Task ActivateMemberAsync(Guid userId, CancellationToken cancellationToken);
}
