using Elaris.Application.Features.Audit.Services.Interfaces;
using Elaris.Domain.Audit.Records;

namespace Elaris.Application.Tests;

internal sealed class CapturingAuditWriter : IAuditWriter
{
    public List<AuditEventWriteModel> Events { get; } = [];

    public Task WriteAsync(AuditEventWriteModel model, CancellationToken cancellationToken)
    {
        Events.Add(model);
        return Task.CompletedTask;
    }
}

internal sealed class NoopAuditWriter : IAuditWriter
{
    public static NoopAuditWriter Instance { get; } = new();

    public Task WriteAsync(AuditEventWriteModel model, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
