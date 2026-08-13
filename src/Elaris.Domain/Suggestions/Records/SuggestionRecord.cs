namespace Elaris.Domain.Suggestions.Records;

public sealed record SuggestionRecord(
    Guid Id,
    string FullName,
    string Email,
    string Phone,
    string Message,
    string? ClientIp,
    string? UserAgent,
    DateTimeOffset CreatedAtUtc);
