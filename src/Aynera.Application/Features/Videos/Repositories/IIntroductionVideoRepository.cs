using Aynera.Domain.Videos.Records;

namespace Aynera.Application.Features.Videos.Repositories;

public interface IIntroductionVideoRepository
{
    Task<IntroductionVideoRecord?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IntroductionVideoRecord> UpsertAsync(
        IntroductionVideoRecord video,
        CancellationToken cancellationToken);

    Task SoftDeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
