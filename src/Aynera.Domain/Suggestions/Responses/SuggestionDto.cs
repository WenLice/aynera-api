namespace Aynera.Domain.Suggestions.Responses;

public sealed record SuggestionDto(
    Guid Id,
    DateTimeOffset CreatedAtUtc);
