using Aynera.Application.Features.Photos.Models;

namespace Aynera.Application.Features.Photos.Services.Interfaces;

public interface IImageProcessor
{
    Task<ProcessedImage> ProcessAsync(
        Stream input,
        string? contentType,
        CancellationToken cancellationToken);
}
