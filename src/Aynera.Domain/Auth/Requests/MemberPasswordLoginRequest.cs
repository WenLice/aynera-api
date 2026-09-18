namespace Aynera.Domain.Auth.Requests;

public sealed record MemberPasswordLoginRequest(string Identifier, string Password);
