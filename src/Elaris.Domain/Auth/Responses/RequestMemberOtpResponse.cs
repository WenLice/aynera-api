namespace Elaris.Domain.Auth.Responses;

public sealed record RequestMemberOtpResponse(int ExpiresInSeconds, int? RetryAfterSeconds = null);
