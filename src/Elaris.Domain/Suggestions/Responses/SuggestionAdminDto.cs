namespace Elaris.Domain.Suggestions.Responses;

public sealed record SuggestionAdminDto(
    Guid Id,
    string FullName,
    string Email,
    string Phone,
    string Message,
    DateTimeOffset CreatedAtUtc);
