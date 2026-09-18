using Aynera.Domain.Media.Responses;

namespace Aynera.Application.Features.Media.Services.Interfaces;

public interface IMediaAuthenticityService
{
    Task<MediaAuthenticityResult> AssessImageAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken);

    Task<MediaAuthenticityResult> AssessVideoAsync(
        Stream content,
        string? contentType,
        CancellationToken cancellationToken);
}
