using Elaris.Domain.Common.Interfaces;

namespace Elaris.Infrastructure.Services;

public sealed class CorrelationIdAccessor : ICorrelationId
{
    public string? Value { get; private set; }

    public void Set(string correlationId) => Value = correlationId;
}
