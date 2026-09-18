namespace Aynera.Domain.Feedback.Responses;

public sealed record FeedbackSubmissionAdminDto(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string Message,
    bool IsExistingUser,
    Guid? MemberId,
    DateTimeOffset CreatedAtUtc);
