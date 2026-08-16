namespace Elaris.Domain.Auth.Requests;

public sealed record ResetMemberPasswordRequest(string Identifier, string Code, string NewPassword);
