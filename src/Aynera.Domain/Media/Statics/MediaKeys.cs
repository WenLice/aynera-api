namespace Aynera.Domain.Media.Statics;

/// <summary>
/// Where each file lives in the bucket — the one home of the naming rule.
/// <para>
/// Every member has their own folder named by user id. Photos carry their slot as a suffix
/// (<c>photo_1</c> … <c>photo_5</c>) so the order is readable in the bucket itself; the videos are
/// one per member and carry none. R2 has no real folders: the <c>{userId}/</c> prefix is shown as one.
/// </para>
/// </summary>
public static class MediaKeys
{
    /// <summary>Photos are always re-encoded to JPEG before storage, so the extension is fixed.</summary>
    public static string Photo(Guid userId, int index) => $"{Folder(userId)}/photo_{index}.jpg";

    public static string IntroVideo(Guid userId, string contentType) =>
        $"{Folder(userId)}/intro_video.{VideoExtension(contentType)}";

    /// <summary>The reference frame from the liveness check. JPEG, one per member, replaced on a re-check.</summary>
    public static string Liveness(Guid userId) => $"{Folder(userId)}/liveness.jpg";

    /// <summary>
    /// A spoken prompt answer, named by the prompt it answers so the bucket reads like the profile.
    /// The id is validated to a path-safe shape (<c>PromptRules.IsValidPromptId</c>) before it gets here.
    /// </summary>
    public static string VoiceAnswer(Guid userId, string promptId, string contentType) =>
        $"{Folder(userId)}/voice_{promptId}.{AudioExtension(contentType)}";

    public static string Folder(Guid userId) => userId.ToString("D");

    /// <summary>Phones record AAC in an MP4 container (<c>.m4a</c>); browsers usually record WebM/Opus.</summary>
    public static string AudioExtension(string contentType) =>
        contentType.Split(';', 2)[0].Trim().ToLowerInvariant() switch
        {
            "audio/webm" => "webm",
            "audio/ogg" => "ogg",
            "audio/mpeg" => "mp3",
            _ => "m4a",
        };

    /// <summary>
    /// The extension follows the real format, so a QuickTime upload is not mislabelled as MP4 when
    /// someone opens the bucket. The stored content type stays the source of truth for serving.
    /// </summary>
    public static string VideoExtension(string contentType) =>
        contentType.Split(';', 2)[0].Trim().ToLowerInvariant() switch
        {
            "video/quicktime" => "mov",
            "video/webm" => "webm",
            _ => "mp4",
        };
}
