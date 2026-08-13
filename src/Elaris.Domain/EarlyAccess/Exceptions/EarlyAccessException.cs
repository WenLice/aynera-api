namespace Elaris.Domain.EarlyAccess.Exceptions;

public sealed class EarlyAccessException : Exception
{
    public EarlyAccessException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}
