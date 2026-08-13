namespace Elaris.Domain.Suggestions.Exceptions;

public sealed class SuggestionException : Exception
{
    public SuggestionException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}
