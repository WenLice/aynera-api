namespace Aynera.Domain.Venues.Exceptions;

public sealed class VenueException : Exception
{
    public VenueException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}
