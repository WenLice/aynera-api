namespace Elaris.Domain.EarlyAccess.Records;

public sealed record EarlyAccessCityRecord(
    Guid Id,
    string Name,
    int Wave,
    int SortOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
