using Aynera.Domain.Feedback.Records;

namespace Aynera.Application.Features.Feedback.Repositories;

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
