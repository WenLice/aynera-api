using Elaris.Domain.Feedback.Requests;
using Elaris.Domain.Feedback.Responses;

namespace Elaris.Application.Features.Feedback.Services.Interfaces;

public interface IFeedbackService
{
    Task<FeedbackSubmissionDto> SubmitAsync(
        SubmitFeedbackRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);
}
