namespace Aynera.Application.Features.Videos.Services.Interfaces;

public interface ISpeechTranscriptionService
{
    Task<string> TranscribeAsync(
        Stream video,
        string contentType,
        CancellationToken cancellationToken);
}
