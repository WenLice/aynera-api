namespace Aynera.Infrastructure.Aws;

/// <summary>
/// AWS access for Rekognition — face liveness and face comparison (<c>Aynera:Aws</c>). The keys belong
/// to an IAM user limited to those calls; the identity pool is what the liveness page streams with.
/// Locally from user secrets; on Render from <c>Aynera__Aws__*</c>. Never commit the keys.
/// </summary>
public sealed class AwsOptions
{
    public const string SectionName = "Aynera:Aws";

    /// <summary>Face Liveness runs in a handful of regions; Mumbai is the one near the members.</summary>
    public string Region { get; set; } = "ap-south-1";
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>The Cognito identity pool whose guest role may only call StartFaceLivenessSession.</summary>
    public string IdentityPoolId { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(Region);
}
