using Elaris.Application.Features.Videos.Models;
using Elaris.Application.Features.Videos.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elaris.Infrastructure.Services;

/// <summary>
/// Dev stub: returns configured <see cref="IntroductionVideoOptions.StubTranscript"/>.
/// Replace with a real speech-to-text provider later.
/// </summary>
public sealed class StubSpeechTranscriptionService : ISpeechTranscriptionService
{
    private readonly IntroductionVideoOptions _options;
    private readonly ILogger<StubSpeechTranscriptionService> _logger;

    public StubSpeechTranscriptionService(
        IOptions<IntroductionVideoOptions> options,
        ILogger<StubSpeechTranscriptionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(
        Stream video,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (video.CanSeek)
        {
            video.Position = 0;
        }

        var buffer = new byte[8192];
        while (await video.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken) > 0)
        {
            // discard until real STT is wired
        }

        var transcript = _options.StubTranscript?.Trim() ?? string.Empty;
        _logger.LogInformation(
            "Stub speech transcription for {ContentType}: length={Length}",
            contentType,
            transcript.Length);

        return transcript;
    }
}
