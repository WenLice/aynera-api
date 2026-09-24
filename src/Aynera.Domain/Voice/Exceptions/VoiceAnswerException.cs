namespace Aynera.Domain.Voice.Exceptions;

public sealed class VoiceAnswerException : Exception
{
    public VoiceAnswerException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}
