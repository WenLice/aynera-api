namespace Aynera.Domain.Auth.Requests;

/// <summary>Step 1 of app registration: prove the mobile number before anything else is asked.</summary>
public sealed record StartPhoneRegistrationRequest(string Phone);
