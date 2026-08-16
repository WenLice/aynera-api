using Elaris.Domain.Audit.Statics;
using Elaris.Domain.Auth.Enums;
using Elaris.Domain.Auth.Exceptions;
using Elaris.Domain.Auth.Records;
using Elaris.Domain.Common.Validation;

namespace Elaris.Domain.Auth.Statics;

public static class LoginIdentifiers
{
    public static LoginIdentifier Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new AuthException(
                "invalid_identifier",
                "Enter a phone number or email.",
                statusCode: 400);
        }

        var trimmed = raw.Trim();
        if (trimmed.Contains('@', StringComparison.Ordinal))
        {
            if (!RequestValidation.BeValidEmail(trimmed))
            {
                throw new AuthException(
                    "invalid_identifier",
                    "Enter a valid email address.",
                    statusCode: 400);
            }

            return new LoginIdentifier(LoginChannel.Email, EmailNormalizer.Normalize(trimmed));
        }

        try
        {
            return new LoginIdentifier(LoginChannel.Phone, PhoneNormalizer.NormalizeIndianMobile(trimmed));
        }
        catch (AuthException)
        {
            throw new AuthException(
                "invalid_identifier",
                "Enter a valid Indian mobile number or email.",
                statusCode: 400);
        }
    }

    public static string Mask(LoginIdentifier identifier) =>
        identifier.Channel == LoginChannel.Phone
            ? AuditRedaction.MaskPhone(identifier.Destination)
            : AuditRedaction.MaskEmail(identifier.Destination) ?? "***";
}
