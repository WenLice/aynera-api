namespace Elaris.Domain.Feedback.Requests;

/// <summary>Grievance / feedback channel (not product suggestions).</summary>
public sealed record SubmitFeedbackRequest(
    string FullName,
    string Email,
    string Message,
    string? Phone = null);
