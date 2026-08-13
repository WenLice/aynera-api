namespace Elaris.Domain.EarlyAccess.Responses;

public sealed record EarlyAccessCityDto(
    Guid Id,
    string Name,
    int Wave,
    int SortOrder,
    bool IsActive);
