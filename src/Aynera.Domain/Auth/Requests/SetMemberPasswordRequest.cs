namespace Aynera.Domain.Auth.Requests;

public sealed record SetMemberPasswordRequest(string Password, string? CurrentPassword = null);
