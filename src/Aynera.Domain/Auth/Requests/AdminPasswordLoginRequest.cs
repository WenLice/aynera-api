namespace Aynera.Domain.Auth.Requests;

public sealed record AdminPasswordLoginRequest(string Identifier, string Password);
