namespace Elaris.Domain.Feedback.Exceptions;

public sealed class FeedbackException : Exception
{
    public FeedbackException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}
