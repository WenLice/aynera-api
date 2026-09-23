using System.Collections.Concurrent;
using Aynera.Application.Features.Media.Storage;

namespace Aynera.Infrastructure.Storage;

/// <summary>
/// Media kept in process memory. For tests only: everything is lost on restart, and a second API
/// instance cannot see what the first stored.
/// </summary>
public sealed class InMemoryMediaStorage : IMediaStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _types = new(StringComparer.Ordinal);

    public Task PutAsync(string key, byte[] data, string contentType, CancellationToken cancellationToken)
    {
        _objects[key] = data.ToArray();
        _types[key] = contentType;
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_objects.TryGetValue(key, out var data) ? data.ToArray() : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        _objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A data URL, so a client can still render the file with nothing but this process running.
    /// It never expires, which is fine here and nowhere else.
    /// </summary>
    public string? GetReadUrl(string key, TimeSpan lifetime) =>
        _objects.TryGetValue(key, out var data)
            ? $"data:{(_types.TryGetValue(key, out var type) ? type : "application/octet-stream")};base64,{Convert.ToBase64String(data)}"
            : null;

    /// <summary>Test hook: the keys currently stored.</summary>
    public IReadOnlyCollection<string> Keys => _objects.Keys.ToArray();
}
