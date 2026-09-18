using Aynera.Application.Features.Auth.Models;
using Aynera.Domain.Auth.Records;

namespace Aynera.Application.Features.Auth.Services.Interfaces;

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
