namespace Aynera.Domain.Auth.Enums;

public enum PasswordCheckResult
{
    Success = 0,
    Invalid = 1,
    LockedOut = 2,
    NotSet = 3
}
