namespace Aynera.Domain.Suggestions.Requests;

public sealed record SubmitSuggestionRequest(
    string FullName,
    string Email,
    string Message,
    string Phone);
