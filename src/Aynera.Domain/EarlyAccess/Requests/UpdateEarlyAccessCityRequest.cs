namespace Aynera.Domain.EarlyAccess.Requests;

public sealed record UpdateEarlyAccessCityRequest(
    string? Name = null,
    int? Wave = null,
    int? SortOrder = null,
    bool? IsActive = null);
