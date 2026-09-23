using Amazon;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime;
using Aynera.Application.Features.Photos.Services.Interfaces;
using Aynera.Domain.Photos.Enums;
using Aynera.Domain.Photos.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Infrastructure.Aws;

/// <summary>
/// Face comparison through AWS Rekognition CompareFaces: is the face in the candidate the person in
/// the reference photo?
/// <para>
/// A candidate with <b>no face</b> is <see cref="FaceMatchStatus.Skipped"/>, not rejected. Three of the
/// five photo slots ask for "your world" and "something you love" — a place, a plate — and a photo
/// with nobody in it is not an impostor. Only a face that is someone else is a mismatch.
/// </para>
/// </summary>
public sealed class AwsFaceMatchService : IFaceMatchService, IDisposable
{
    /// <summary>Similarity (0–100) at or above which two faces count as the same person.</summary>
    public const float MatchThreshold = 90f;

    private readonly AmazonRekognitionClient _client;
    private readonly ILogger<AwsFaceMatchService> _logger;

    public AwsFaceMatchService(IOptions<AwsOptions> options, ILogger<AwsFaceMatchService> logger)
    {
        var aws = options.Value;
        _logger = logger;
        _client = new AmazonRekognitionClient(
            new BasicAWSCredentials(aws.AccessKeyId, aws.SecretAccessKey),
            RegionEndpoint.GetBySystemName(aws.Region));
    }

    public async Task<FaceMatchResult> CompareAsync(
        byte[] referenceImage,
        string referenceContentType,
        byte[] candidateImage,
        string candidateContentType,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.CompareFacesAsync(
                new CompareFacesRequest
                {
                    SourceImage = new Image { Bytes = new MemoryStream(referenceImage) },
                    TargetImage = new Image { Bytes = new MemoryStream(candidateImage) },
                    // Every candidate face comes back with its score; the decision is made below.
                    SimilarityThreshold = 0f,
                },
                cancellationToken);

            var matches = response.FaceMatches ?? [];
            var unmatched = response.UnmatchedFaces ?? [];
            if (matches.Count == 0 && unmatched.Count == 0)
            {
                return new FaceMatchResult(FaceMatchStatus.Skipped.ToString(), null, "No face in this photo.");
            }

            var best = matches.Count == 0 ? 0f : matches.Max(m => m.Similarity);
            var score = Math.Round((decimal)best, 2);
            return best >= MatchThreshold
                ? new FaceMatchResult(FaceMatchStatus.Matched.ToString(), score)
                : new FaceMatchResult(
                    FaceMatchStatus.Rejected.ToString(),
                    score,
                    "This doesn't look like the person in your first photo.");
        }
        catch (InvalidParameterException ex)
        {
            // Rekognition's answer when an image has no detectable face. The reference photo is the
            // member's face shot, so in practice this is the candidate — a place, a plate, a view.
            _logger.LogInformation("CompareFaces found no face: {Message}", ex.Message);
            return new FaceMatchResult(FaceMatchStatus.Skipped.ToString(), null, "No face in this photo.");
        }
    }

    public void Dispose() => _client.Dispose();
}
