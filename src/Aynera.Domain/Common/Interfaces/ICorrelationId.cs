namespace Aynera.Domain.Common.Interfaces;

/// <summary>Request-scoped correlation id from X-Correlation-Id (or generated).</summary>
public interface ICorrelationId
{
    string? Value { get; }
}
