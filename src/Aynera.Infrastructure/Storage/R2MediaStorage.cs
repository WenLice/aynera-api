using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Aynera.Application.Features.Media.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aynera.Infrastructure.Storage;

/// <summary>
/// Media bytes in a private Cloudflare R2 bucket, through R2's S3-compatible API.
/// <para>
/// <c>DisablePayloadSigning</c> and <c>DisableDefaultChecksumValidation</c> are required: R2 does
/// not support the streaming SigV4 variant the SDK uses by default, and uploads fail without them.
/// </para>
/// </summary>
public sealed class R2MediaStorage : IMediaStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly ILogger<R2MediaStorage> _logger;

    public R2MediaStorage(IOptions<R2Options> options, ILogger<R2MediaStorage> logger)
    {
        var r2 = options.Value;
        _bucket = r2.Bucket;
        _logger = logger;
        // Signed links must use SigV4; R2 rejects the older scheme.
        AWSConfigsS3.UseSignatureVersion4 = true;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey),
            new AmazonS3Config { ServiceURL = r2.ServiceUrl });
    }

    public async Task PutAsync(string key, byte[] data, string contentType, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream(data, writable: false);
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = body,
                ContentType = contentType,
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true,
            },
            cancellationToken);

        _logger.LogInformation("Stored media {Key} ({Bytes} bytes)", key, data.Length);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetObjectAsync(_bucket, key, cancellationToken);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Media {Key} is recorded but missing from the bucket", key);
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        // S3 semantics: deleting a missing key succeeds, so this is safe to repeat.
        await _client.DeleteObjectAsync(_bucket, key, cancellationToken);
        _logger.LogInformation("Deleted media {Key}", key);
    }

    public string? GetReadUrl(string key, TimeSpan lifetime) =>
        _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime),
        });

    public void Dispose() => _client.Dispose();
}
