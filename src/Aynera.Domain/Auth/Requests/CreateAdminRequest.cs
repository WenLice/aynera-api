namespace Aynera.Domain.Auth.Requests;

public sealed record CreateAdminRequest(
    string Email,
    string Password,
    string? Phone = null);
