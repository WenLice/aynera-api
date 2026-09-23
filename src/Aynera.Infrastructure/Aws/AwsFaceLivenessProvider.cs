using Amazon;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime;
using Aynera.Application.Features.Liveness.Models;
using Aynera.Application.Features.Liveness.Services.Interfaces;
using Aynera.Domain.Liveness.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Infrastructure.Aws;

/// <summary>AWS Rekognition Face Liveness. The video never reaches this server.</summary>
public sealed class AwsFaceLivenessProvider : IFaceLivenessProvider, IDisposable
{
    private readonly AmazonRekognitionClient _client;
    private readonly AwsOptions _options;
    private readonly ILogger<AwsFaceLivenessProvider> _logger;

    public AwsFaceLivenessProvider(IOptions<AwsOptions> options, ILogger<AwsFaceLivenessProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new AmazonRekognitionClient(
            new BasicAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey),
            RegionEndpoint.GetBySystemName(_options.Region));
    }

    public string Region => _options.Region;

    public string IdentityPoolId => string.IsNullOrWhiteSpace(_options.IdentityPoolId)
        ? throw new LivenessException(
            "liveness_unavailable",
            "The face check is not set up yet.",
            statusCode: 503)
        : _options.IdentityPoolId;

    public async Task<string> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var response = await _client.CreateFaceLivenessSessionAsync(
            new CreateFaceLivenessSessionRequest
            {
                ClientRequestToken = Guid.NewGuid().ToString("N"),
                Settings = new CreateFaceLivenessSessionRequestSettings { AuditImagesLimit = 0 },
            },
            cancellationToken);

        return response.SessionId;
    }

    public async Task<FaceLivenessResult> GetResultAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetFaceLivenessSessionResultsAsync(
                new GetFaceLivenessSessionResultsRequest { SessionId = sessionId },
                cancellationToken);

            // No S3 output is configured, so the reference frame comes back inline.
            var bytes = response.ReferenceImage?.Bytes?.ToArray();
            return new FaceLivenessResult(
                response.Status?.Value ?? "FAILED",
                response.Confidence is float confidence ? (decimal)confidence : null,
                bytes is { Length: > 0 } ? bytes : null);
        }
        catch (SessionNotFoundException)
        {
            _logger.LogWarning("Liveness session {SessionId} not found at AWS (likely expired)", sessionId);
            return new FaceLivenessResult("EXPIRED", null, null);
        }
    }

    public void Dispose() => _client.Dispose();
}
