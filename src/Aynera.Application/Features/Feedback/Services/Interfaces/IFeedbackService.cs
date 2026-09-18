using Aynera.Domain.Common;
using Aynera.Domain.Feedback.Requests;
using Aynera.Domain.Feedback.Responses;

namespace Aynera.Application.Features.Feedback.Services.Interfaces;

public interface IFeedbackService
{
    Task<PagedResult<FeedbackSubmissionAdminDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken);

    Task<FeedbackSubmissionDto> SubmitAsync(
        SubmitFeedbackRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);
}
