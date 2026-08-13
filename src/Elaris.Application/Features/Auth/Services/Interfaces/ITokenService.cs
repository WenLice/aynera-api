using Elaris.Application.Features.Auth.Models;
using Elaris.Domain.Auth.Records;

namespace Elaris.Application.Features.Auth.Services.Interfaces;

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(
        UserRecord user,
        string audience,
        Guid sessionId,
        string amr,
        DateTimeOffset authTimeUtc);

    IssuedRefreshToken CreateRefreshToken();
}
