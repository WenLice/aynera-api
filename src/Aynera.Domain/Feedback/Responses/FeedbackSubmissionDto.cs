namespace Aynera.Domain.Feedback.Responses;

public sealed record FeedbackSubmissionDto(
    Guid Id,
    bool IsExistingUser,
    DateTimeOffset CreatedAtUtc);
