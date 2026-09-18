namespace Aynera.Domain.Auth.Requests;

public sealed record RecoverMemberRequest(string Identifier, string Code);
