namespace Elaris.Domain.Auth.Requests;

public sealed record VerifyMemberOtpRequest(
    string Phone,
    string Code,
    string? Audience = null);
