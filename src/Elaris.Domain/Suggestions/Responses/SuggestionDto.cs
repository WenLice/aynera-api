namespace Elaris.Domain.Suggestions.Responses;

public sealed record SuggestionDto(
    Guid Id,
    DateTimeOffset CreatedAtUtc);
