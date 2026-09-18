using Aynera.Domain.Common;

namespace Aynera.Application.Features.Suggestions.Models;

public sealed class SuggestionOptions
{
    public const string SectionName = "Aynera:Suggestions";

    public int MaxMessageLength { get; set; } = PublicFormLimits.MaxMessageLength;

    public int MaxRequestsPerIpPerHour { get; set; } = 20;
}
