namespace Elaris.Application.Features.EarlyAccess.Models;

public sealed class EarlyAccessOptions
{
    public const string SectionName = "Elaris:EarlyAccess";

    public int MaxRequestsPerIpPerHour { get; set; } = 30;
}
