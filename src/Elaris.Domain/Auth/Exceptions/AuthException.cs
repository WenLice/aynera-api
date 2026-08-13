namespace Elaris.Domain.Auth.Exceptions;

public sealed class AuthException : Exception
{
    public AuthException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}
