namespace Aynera.Domain.Auth.Requests;

public sealed record VerifyMemberOtpRequest(
    string Identifier,
    string Code,
    string? Audience = null);
