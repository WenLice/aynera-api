namespace Aynera.Domain.Auth.Statics;

public static class EmailNormalizer
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
