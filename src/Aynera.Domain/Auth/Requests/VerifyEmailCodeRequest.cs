namespace Aynera.Domain.Auth.Requests;

/// <summary>Signed-in member proves the email with the emailed code; the email is then set and confirmed.</summary>
public sealed record VerifyEmailCodeRequest(string Email, string Code);
