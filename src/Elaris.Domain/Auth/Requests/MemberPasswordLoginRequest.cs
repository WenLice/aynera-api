namespace Elaris.Domain.Auth.Requests;

public sealed record MemberPasswordLoginRequest(string Identifier, string Password);
