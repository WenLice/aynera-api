using System.Net.Mail;
using System.Text.RegularExpressions;
using Aynera.Domain.EarlyAccess.Enums;

namespace Aynera.Domain.Common.Validation;

public static partial class RequestValidation
{
    public static bool BeValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        return MailAddress.TryCreate(trimmed, out var parsed)
            && string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase);
    }

    public static bool BeValidLoginIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        var trimmed = identifier.Trim();
        return trimmed.Contains('@', StringComparison.Ordinal)
            ? BeValidEmail(trimmed)
            : BeValidIndianMobile(trimmed);
    }

    public static bool BeValidIndianMobile(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        var digits = DigitsOnly().Replace(phone, string.Empty);
        if (digits.StartsWith("91", StringComparison.Ordinal) && digits.Length == 12)
        {
            digits = digits[2..];
        }

        return digits.Length == 10 && digits[0] is >= '6' and <= '9';
    }

    public static bool BeKnownEarlyAccessInterest(string? interest)
    {
        if (string.IsNullOrWhiteSpace(interest))
        {
            return false;
        }

        var normalized = interest.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<EarlyAccessInterest>(normalized, ignoreCase: true, out _);
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitsOnly();
}
