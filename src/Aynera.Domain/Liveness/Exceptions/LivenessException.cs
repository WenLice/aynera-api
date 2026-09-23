namespace Aynera.Domain.Liveness.Exceptions;

public sealed class LivenessException : Exception
{
    public LivenessException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}
