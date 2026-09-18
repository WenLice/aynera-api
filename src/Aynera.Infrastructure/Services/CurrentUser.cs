using Aynera.Domain.Auth.Exceptions;
using Aynera.Domain.Common.Interfaces;

namespace Aynera.Infrastructure.Services;

public sealed class CurrentUser : ICurrentUser
{
    public Guid? UserId { get; private set; }

    public bool IsAuthenticated => UserId.HasValue;

    public void SetUserId(Guid userId) => UserId = userId;

    public Guid GetRequiredUserId() =>
        UserId ?? throw new AuthException(
            "unauthorized",
            "Authentication required.",
            statusCode: 401);
}
