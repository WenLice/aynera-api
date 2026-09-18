namespace Aynera.Domain.Admissions.Exceptions;

public sealed class AdmissionException : Exception
{
    public AdmissionException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}
