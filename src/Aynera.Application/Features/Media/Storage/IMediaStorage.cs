namespace Aynera.Application.Features.Media.Storage;

/// <summary>
/// Where media bytes live. The database keeps only the key; the bytes sit in object storage
/// (Cloudflare R2 in every real environment, an in-memory store in tests).
/// <para>
/// Uploads still pass through the API so the photo and video checks run on the real bytes before
/// anything is written here — this is a place to put files, not a way around those checks.
/// </para>
/// </summary>
public interface IMediaStorage
{
    /// <summary>Writes the file, replacing whatever was stored under the same key.</summary>
    Task PutAsync(string key, byte[] data, string contentType, CancellationToken cancellationToken);

    /// <summary>The file's bytes, or null when nothing is stored under the key.</summary>
    Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Removes the file. Deleting a key that does not exist is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// A link a client can open directly for <paramref name="lifetime"/>. The bucket stays private:
    /// the link is signed and expires, so it is handed out per request and never stored.
    /// </summary>
    string? GetReadUrl(string key, TimeSpan lifetime);
}
