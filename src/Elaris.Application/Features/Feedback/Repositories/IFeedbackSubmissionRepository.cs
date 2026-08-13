using Elaris.Domain.Feedback.Records;

namespace Elaris.Application.Features.Feedback.Repositories;

public interface IFeedbackSubmissionRepository
{
    Task<FeedbackSubmissionRecord> AddAsync(
        FeedbackSubmissionRecord feedback,
        CancellationToken cancellationToken);
}
