namespace Aynera.Domain.Auth.Requests;

/// <summary>Signed-in member asks for a code at the email they want on the account.</summary>
public sealed record StartEmailVerificationRequest(string Email);
