using Elaris.Domain.Common;

namespace Elaris.Application.Features.Suggestions.Models;

public sealed class SuggestionOptions
{
    public const string SectionName = "Elaris:Suggestions";

    public int MaxMessageLength { get; set; } = PublicFormLimits.MaxMessageLength;

    public int MaxRequestsPerIpPerHour { get; set; } = 20;
}
