using Aynera.Application.Features.Media.Storage;

namespace Aynera.Application.Tests;

/// <summary>For service tests that never read bytes back: signed links are simply absent.</summary>
internal sealed class NullMediaStorage : IMediaStorage
{
    public static readonly NullMediaStorage Instance = new();

    public Task PutAsync(string key, byte[] data, string contentType, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
    public string? GetReadUrl(string key, TimeSpan lifetime) => null;
}
