namespace Aynera.Application.Features.EarlyAccess.Models;

public sealed class EarlyAccessOptions
{
    public const string SectionName = "Aynera:EarlyAccess";

    public int MaxRequestsPerIpPerHour { get; set; } = 30;
}
