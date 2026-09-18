using Aynera.Domain.Common;

namespace Aynera.Application.Features.Feedback.Models;

public sealed class FeedbackOptions
{
    public const string SectionName = "Aynera:Feedback";

    public int MaxMessageLength { get; set; } = PublicFormLimits.MaxMessageLength;

    public int MaxRequestsPerIpPerHour { get; set; } = 20;
}
