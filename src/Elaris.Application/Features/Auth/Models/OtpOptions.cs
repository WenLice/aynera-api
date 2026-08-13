namespace Elaris.Application.Features.Auth.Models;

public sealed class OtpOptions
{
    public const string SectionName = "Elaris:Otp";

    public int CodeLength { get; set; } = 6;
    public int TtlSeconds { get; set; } = 300;
    public int MaxAttempts { get; set; } = 5;
    public int MaxRequestsPerPhonePerHour { get; set; } = 5;
    public int MaxRequestsPerIpPerHour { get; set; } = 20;
}
