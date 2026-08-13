using Microsoft.Extensions.Logging;

namespace Elaris.Application.Tests;

/// <summary>No-op <see cref="ILogger{T}"/> for unit tests.</summary>
internal sealed class DiscardLogger<T> : ILogger<T>
{
    public static DiscardLogger<T> Instance { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
