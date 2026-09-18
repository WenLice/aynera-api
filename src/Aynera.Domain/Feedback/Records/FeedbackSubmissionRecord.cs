namespace Aynera.Domain.Feedback.Records;

public sealed record FeedbackSubmissionRecord(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string Message,
    string? ClientIp,
    string? UserAgent,
    bool IsExistingUser,
    Guid? MemberId,
    DateTimeOffset CreatedAtUtc);
