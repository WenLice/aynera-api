namespace Aynera.Infrastructure.Storage;

/// <summary>
/// Cloudflare R2 bucket access (<c>Aynera:R2</c>). Locally these come from user secrets; on Render
/// from <c>Aynera__R2__*</c> environment variables. Never commit them.
/// </summary>
public sealed class R2Options
{
    public const string SectionName = "Aynera:R2";

    public string AccountId { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(Bucket);

    /// <summary>The S3-compatible endpoint for this account.</summary>
    public string ServiceUrl => $"https://{AccountId}.r2.cloudflarestorage.com";
}
