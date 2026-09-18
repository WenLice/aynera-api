using Aynera.Domain.Venues.Enums;

namespace Aynera.Domain.Venues.Validators;

public static class VenueValidation
{
    public static DateOnly TodayInIndia => DateOnly.FromDateTime(
        DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
    public static bool BeKnownVenueType(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Enum.TryParse<VenueType>(value, ignoreCase: true, out _);

    public static bool TryParseType(string? value, out VenueType type) =>
        Enum.TryParse(value, ignoreCase: true, out type);
}
