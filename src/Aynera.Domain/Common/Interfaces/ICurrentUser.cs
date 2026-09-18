namespace Aynera.Domain.Common.Interfaces;

/// <summary>
/// Request-scoped identity. Populated by API middleware after JWT authentication.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    /// <summary>Returns the authenticated user id, or throws when missing.</summary>
    Guid GetRequiredUserId();
}
