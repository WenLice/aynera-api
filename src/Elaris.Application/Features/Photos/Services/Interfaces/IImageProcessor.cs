using Elaris.Application.Features.Photos.Models;

namespace Elaris.Application.Features.Photos.Services.Interfaces;

public interface IImageProcessor
{
    Task<ProcessedImage> ProcessAsync(
        Stream input,
        string? contentType,
        CancellationToken cancellationToken);
}
