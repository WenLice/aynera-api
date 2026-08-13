using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Elaris.Domain.Auth.Exceptions;

namespace Elaris.Domain.Auth.Statics;

public static partial class PhoneNormalizer
{
    public static string NormalizeIndianMobile(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new AuthException("invalid_phone", "Phone number is required.");
        }

        var digits = DigitsOnly().Replace(phone, string.Empty);

        if (digits.StartsWith("91", StringComparison.Ordinal) && digits.Length == 12)
        {
            digits = digits[2..];
        }

        if (digits.Length != 10 || digits[0] is < '6' or > '9')
        {
            throw new AuthException("invalid_phone", "Enter a valid Indian mobile number.");
        }

        return $"+91{digits}";
    }

    public static string Mask(string phoneE164)
    {
        if (phoneE164.Length < 4)
        {
            return "****";
        }

        return $"{phoneE164[..3]}******{phoneE164[^4..]}";
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitsOnly();
}

public static class TokenHasher
{
    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public static class OtpCodeGenerator
{
    public static string Generate(int length)
    {
        if (length is < 4 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return new string(chars);
    }
}

public static class SecureEquals
{
    public static bool Hex(string left, string right)
    {
        try
        {
            var a = Convert.FromHexString(left);
            var b = Convert.FromHexString(right);
            return a.Length == b.Length
                && CryptographicOperations.FixedTimeEquals(a, b);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class AgeRules
{
    public const int MinimumAgeYears = 18;

    public static void EnsureAdult(DateOnly dateOfBirth, DateOnly? today = null)
    {
        var asOf = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var eighteenthBirthday = dateOfBirth.AddYears(MinimumAgeYears);
        if (eighteenthBirthday > asOf)
        {
            throw new AuthException(
                "underage",
                $"You must be at least {MinimumAgeYears} years old to register.",
                statusCode: 400);
        }

        if (dateOfBirth > asOf)
        {
            throw new AuthException("invalid_date_of_birth", "Date of birth cannot be in the future.");
        }
    }
}
