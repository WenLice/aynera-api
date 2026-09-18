namespace Aynera.Domain.Auth.Statics;

public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    public static bool IsWellFormed(string? password)
    {
        if (string.IsNullOrEmpty(password)
            || password.Length < MinLength
            || password.Length > MaxLength)
        {
            return false;
        }

        var hasDigit = false;
        var hasLower = false;
        foreach (var c in password)
        {
            if (char.IsDigit(c))
            {
                hasDigit = true;
            }
            else if (char.IsLower(c))
            {
                hasLower = true;
            }
        }

        return hasDigit && hasLower;
    }
}
