using Aynera.Application.Features.Photos.Models;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Domain.Photos.Responses;
using Aynera.Domain.Photos.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Infrastructure.Services;

public sealed class StubFaceMatchService : IFaceMatchService
{
    private readonly PhotoOptions _options;
    private readonly ILogger<StubFaceMatchService> _logger;

    public StubFaceMatchService(IOptions<PhotoOptions> options, ILogger<StubFaceMatchService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<FaceMatchResult> CompareAsync(
        byte[] referenceImage,
        string referenceContentType,
        byte[] candidateImage,
        string candidateContentType,
        CancellationToken cancellationToken)
    {
        var raw = string.IsNullOrWhiteSpace(_options.StubFaceMatchStatus)
            ? FaceMatchStatus.Matched.ToString()
            : _options.StubFaceMatchStatus.Trim();

        if (!Enum.TryParse<FaceMatchStatus>(raw, ignoreCase: true, out var status))
        {
            status = FaceMatchStatus.Matched;
        }

        _logger.LogInformation(
            "Stub face match: status={Status} referenceBytes={ReferenceBytes} candidateBytes={CandidateBytes}",
            status,
            referenceImage.Length,
            candidateImage.Length);

        var score = status is FaceMatchStatus.Matched or FaceMatchStatus.Skipped ? 99.0m : 10.0m;
        var detail = status == FaceMatchStatus.Rejected
            ? "Stub face match rejected this photo."
            : null;

        return Task.FromResult(new FaceMatchResult(status.ToString(), score, detail));
    }
}
