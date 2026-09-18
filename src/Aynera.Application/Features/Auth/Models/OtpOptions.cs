namespace Aynera.Application.Features.Auth.Models;

public sealed class OtpOptions
{
    public const string SectionName = "Aynera:Otp";

    public int CodeLength { get; set; } = 6;
    public int TtlSeconds { get; set; } = 300;
    public int MaxAttempts { get; set; } = 5;
    public int MaxRequestsPerPhonePerHour { get; set; } = 5;
    public int MaxRequestsPerIpPerHour { get; set; } = 20;

    /// <summary>
    /// Development convenience only: the Console SMS/email providers print the code they would
    /// have sent, so a flow can be completed by hand. Forced off outside the Development
    /// environment at startup; never enable it where real providers run.
    /// </summary>
    public bool RevealCodesInLogs { get; set; }
}
