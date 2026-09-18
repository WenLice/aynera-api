namespace Aynera.Domain.Auth.Statics;

public static class OtpPurposes
{
    public const string Login = "login";
    public const string AdminLogin = "admin_login";
    public const string PasswordReset = "password_reset";
    public const string Reactivation = "reactivation";
    /// <summary>Phone ownership proof that creates the member account (step-wise registration).</summary>
    public const string Registration = "registration";
    /// <summary>Email ownership proof by code for a signed-in member (step-wise registration).</summary>
    public const string EmailVerification = "email_verification";
}
