namespace Aynera.Domain.Auth.Requests;

public sealed record VerifyAdminOtpRequest(string Identifier, string Code);
