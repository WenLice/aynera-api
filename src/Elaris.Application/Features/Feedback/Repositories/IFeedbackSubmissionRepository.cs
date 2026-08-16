using Elaris.Domain.Feedback.Records;

namespace Elaris.Application.Features.Feedback.Repositories;

public interface IFeedbackSubmissionRepository
{
    Task<(IReadOnlyList<FeedbackSubmissionRecord> Items, int TotalCount)> ListPageAsync(
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<FeedbackSubmissionRecord> AddAsync(
        FeedbackSubmissionRecord feedback,
        CancellationToken cancellationToken);
}
