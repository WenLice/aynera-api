namespace Aynera.Domain.Auth.Requests;

/// <summary>Step 2 of app registration: the SMS code creates the member account and signs it in.</summary>
public sealed record VerifyPhoneRegistrationRequest(string Phone, string Code);
