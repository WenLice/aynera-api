using Elaris.Domain.Audit.Records;

namespace Elaris.Application.Features.Audit.Services.Interfaces;

public interface IAuditWriter
{
    /// <summary>Writes AuditLog + AuditEvent. Failures are swallowed after logging.</summary>
    Task WriteAsync(AuditEventWriteModel model, CancellationToken cancellationToken);
}
