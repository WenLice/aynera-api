using Elaris.Application.Features.Videos.Models;
using Elaris.Domain.Videos.Responses;

namespace Elaris.Application.Features.Videos.Services.Interfaces;

public interface IIntroductionVideoService
{
    Task<IntroductionVideoDto> UploadAsync(
        Guid userId,
        VideoUploadInput file,
        CancellationToken cancellationToken);

    Task<IntroductionVideoDto?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<IntroductionVideoBytes> GetBytesAsync(Guid userId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);
}
