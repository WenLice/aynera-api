namespace Elaris.Domain.EarlyAccess.Requests;

public sealed record CreateEarlyAccessCityRequest(
    string Name,
    int Wave = 1,
    int SortOrder = 0,
    bool IsActive = true);
