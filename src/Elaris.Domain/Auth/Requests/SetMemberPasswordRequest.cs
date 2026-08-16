namespace Elaris.Domain.Auth.Requests;

public sealed record SetMemberPasswordRequest(string Password, string? CurrentPassword = null);
