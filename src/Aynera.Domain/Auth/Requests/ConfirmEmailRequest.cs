namespace Aynera.Domain.Auth.Requests;

public sealed record ConfirmEmailRequest(
    Guid UserId,
    string Token);
