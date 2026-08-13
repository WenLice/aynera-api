using Elaris.Domain.Common;

namespace Elaris.Application.Features.Feedback.Models;

public sealed class FeedbackOptions
{
    public const string SectionName = "Elaris:Feedback";

    public int MaxMessageLength { get; set; } = PublicFormLimits.MaxMessageLength;

    public int MaxRequestsPerIpPerHour { get; set; } = 20;
}
