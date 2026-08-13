namespace Elaris.Domain.Auth.Responses;

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresInSeconds,
    AuthAccountDto Account);
