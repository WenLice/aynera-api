using Aynera.Domain.Photos.Responses;

namespace Aynera.Application.Features.Photos.Services.Interfaces;

public interface IFaceMatchService
{
    Task<FaceMatchResult> CompareAsync(
        byte[] referenceImage,
        string referenceContentType,
        byte[] candidateImage,
        string candidateContentType,
        CancellationToken cancellationToken);
}
